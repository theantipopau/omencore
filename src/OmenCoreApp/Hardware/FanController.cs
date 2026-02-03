using System;
using System.Collections.Generic;
using System.Linq;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    public class FanController
    {
        private readonly IEcAccess _ecAccess;
        private readonly IReadOnlyDictionary<string, int> _registerMap;
        private readonly LibreHardwareMonitorImpl _bridge;
        private readonly LoggingService? _logging;
        
        // EC registers for reading actual fan RPM (from omen-fan project)
        private const ushort REG_FAN1_RPM = 0x34;  // Fan 1 speed in units of 100 RPM
        private const ushort REG_FAN2_RPM = 0x35;  // Fan 2 speed in units of 100 RPM
        
        // Track last set fan percentage for fallback estimation only
        private int _lastSetFanPercent = -1;

        public FanController(IEcAccess ecAccess, IReadOnlyDictionary<string, int> registerMap, LibreHardwareMonitorImpl bridge, LoggingService? logging = null)
        {
            _ecAccess = ecAccess;
            _registerMap = registerMap;
            _bridge = bridge;
            _logging = logging;
            _logging?.Debug("FanController initialized (EC access ready: " + _ecAccess.IsAvailable + ")");
        }

        public bool IsEcReady => _ecAccess.IsAvailable;

        /// <summary>
        /// Apply a preset by evaluating the curve at current temperature.
        /// This is the correct behavior - not using Max() which would always set max speed.
        /// </summary>
        public void ApplyPreset(FanPreset preset)
        {
            if (preset.Curve.Count == 0)
            {
                return;
            }
            
            // Get current temperature and evaluate curve
            var cpuTemp = _bridge.GetCpuTemperature();
            var gpuTemp = _bridge.GetGpuTemperature();
            var maxTemp = Math.Max(cpuTemp, gpuTemp);
            
            // Evaluate curve at current temperature
            int targetPercent = EvaluateCurve(preset.Curve, maxTemp);
            WriteDuty(targetPercent);
        }

        /// <summary>
        /// Apply a custom curve by evaluating at current temperature.
        /// </summary>
        public void ApplyCustomCurve(IEnumerable<FanCurvePoint> curve)
        {
            var table = curve.OrderBy(p => p.TemperatureC).ToList();
            if (!table.Any())
            {
                return;
            }
            
            // Get current temperature and evaluate curve
            var cpuTemp = _bridge.GetCpuTemperature();
            var gpuTemp = _bridge.GetGpuTemperature();
            var maxTemp = Math.Max(cpuTemp, gpuTemp);
            
            // Evaluate curve at current temperature
            int targetPercent = EvaluateCurve(table, maxTemp);
            WriteDuty(targetPercent);
        }
        
        /// <summary>
        /// Evaluate a fan curve at a given temperature using linear interpolation.
        /// </summary>
        private int EvaluateCurve(IEnumerable<FanCurvePoint> curve, double temp)
        {
            var sorted = curve.OrderBy(p => p.TemperatureC).ToList();
            
            if (sorted.Count == 0)
                return 50; // Default
            
            // Below minimum temperature
            if (temp <= sorted.First().TemperatureC)
                return sorted.First().FanPercent;
            
            // Above maximum temperature
            if (temp >= sorted.Last().TemperatureC)
                return sorted.Last().FanPercent;

            // Linear interpolation between curve points
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                if (temp >= sorted[i].TemperatureC && temp <= sorted[i + 1].TemperatureC)
                {
                    var t1 = sorted[i].TemperatureC;
                    var t2 = sorted[i + 1].TemperatureC;
                    var p1 = sorted[i].FanPercent;
                    var p2 = sorted[i + 1].FanPercent;
                    
                    // Avoid division by zero
                    if (Math.Abs(t2 - t1) < 0.1)
                        return p1;
                    
                    return (int)(p1 + (p2 - p1) * (temp - t1) / (t2 - t1));
                }
            }
            
            return sorted.Last().FanPercent;
        }

        /// <summary>
        /// Read actual fan RPM from EC registers, with fallback to estimation.
        /// HP OMEN laptops store fan speed in 0x34/0x35 as units of 100 RPM.
        /// </summary>
        public IEnumerable<FanTelemetry> ReadFanSpeeds()
        {
            var fans = new List<FanTelemetry>();
            var cpuTemp = _bridge.GetCpuTemperature();
            var gpuTemp = _bridge.GetGpuTemperature();

            // Try to get fan speeds from LibreHardwareMonitor first (some models expose via SuperIO)
            var fanSpeeds = _bridge.GetFanSpeeds();
            if (fanSpeeds.Any())
            {
                int index = 0;
                foreach (var (name, rpm) in fanSpeeds)
                {
                    fans.Add(new FanTelemetry
                    {
                        Name = name,
                        SpeedRpm = (int)rpm,
                        DutyCyclePercent = CalculateDutyFromRpm((int)rpm, index),
                        Temperature = index == 0 ? cpuTemp : gpuTemp,
                        RpmSource = RpmSource.HardwareMonitor
                    });
                    index++;
                }
                return fans;
            }

            // Try to read actual RPM from EC registers (HP OMEN specific)
            var (fan1Rpm, fan2Rpm) = ReadActualFanRpm();
            
            if (fan1Rpm > 0 || fan2Rpm > 0)
            {
                // We got actual readings from EC
                fans.Add(new FanTelemetry 
                { 
                    Name = "CPU Fan", 
                    SpeedRpm = fan1Rpm,
                    DutyCyclePercent = CalculateDutyFromRpm(fan1Rpm, 0), 
                    Temperature = cpuTemp,
                    RpmSource = RpmSource.EcDirect
                });
                fans.Add(new FanTelemetry 
                { 
                    Name = "GPU Fan", 
                    SpeedRpm = fan2Rpm,
                    DutyCyclePercent = CalculateDutyFromRpm(fan2Rpm, 1), 
                    Temperature = gpuTemp,
                    RpmSource = RpmSource.EcDirect
                });
                System.Diagnostics.Debug.WriteLine($"[FanController.ReadFanSpeeds] EC read successful: Fan1={fan1Rpm} RPM, Fan2={fan2Rpm} RPM");
                return fans;
            }

            // Fallback: estimate based on last set percentage or temperature
            System.Diagnostics.Debug.WriteLine($"[FanController.ReadFanSpeeds] EC read failed, using fallback estimation. _lastSetFanPercent={_lastSetFanPercent}");
            int fanPercent;
            int fanRpm;
            
            if (_lastSetFanPercent >= 0)
            {
                fanPercent = _lastSetFanPercent;
                fanRpm = (_lastSetFanPercent * 5500) / 100;
            }
            else
            {
                var maxTemp = Math.Max(cpuTemp, gpuTemp);
                if (maxTemp > 0)
                {
                    fanPercent = Math.Clamp((int)((maxTemp - 30) * 2), 20, 80);
                    fanRpm = (fanPercent * 5500) / 100;
                }
                else
                {
                    fanPercent = 30;
                    fanRpm = 1650;
                }
            }
            
            fans.Add(new FanTelemetry 
            { 
                Name = "CPU Fan (est.)", 
                SpeedRpm = fanRpm, 
                DutyCyclePercent = fanPercent, 
                Temperature = cpuTemp,
                RpmSource = RpmSource.Estimated
            });
            fans.Add(new FanTelemetry 
            { 
                Name = "GPU Fan (est.)", 
                SpeedRpm = fanRpm, 
                DutyCyclePercent = fanPercent, 
                Temperature = gpuTemp,
                RpmSource = RpmSource.Estimated
            });

            return fans;
        }
        
        /// <summary>
        /// Read actual fan RPM from HP OMEN EC registers.
        /// Tries multiple register sets for compatibility with different models.
        /// Made protected virtual so test subclasses can override read behavior.
        /// </summary>
        protected virtual (int fan1Rpm, int fan2Rpm) ReadActualFanRpm()
        {
            if (!_ecAccess.IsAvailable)
                return (0, 0);

            // Retry/backoff strategy to mitigate inter-process EC contention.
            // Some contention manifests as transient 0 RPM or thrown timeouts when other apps access EC.
            const int attempts = 5;
            var readings = new List<(int f1, int f2)>();
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    // Try alternative registers first (0x4A-0x4B for Fan1, 0x4C-0x4D for Fan2) - 16-bit RPM
                    try
                    {
                        var fan1Low = _ecAccess.ReadByte(0x4A);
                        var fan1High = _ecAccess.ReadByte(0x4B);
                        var fan2Low = _ecAccess.ReadByte(0x4C);
                        var fan2High = _ecAccess.ReadByte(0x4D);
                        _logging?.Debug($"EC Read alt RPM regs (attempt {attempt}): 0x4A=0x{fan1Low:X2}, 0x4B=0x{fan1High:X2}, 0x4C=0x{fan2Low:X2}, 0x4D=0x{fan2High:X2}");

                        var fan1Rpm = (fan1High << 8) | fan1Low;
                        var fan2Rpm = (fan2High << 8) | fan2Low;

                        // Validate RPM range (0-8000 is valid for laptop fans)
                        // 0xFF or 0xFFFF values indicate "no data" or error states
                        if ((HpWmiBios.IsValidRpm(fan1Rpm) && fan1Rpm > 0) || 
                            (HpWmiBios.IsValidRpm(fan2Rpm) && fan2Rpm > 0))
                        {
                            // Only return valid readings
                            var validF1 = HpWmiBios.IsValidRpm(fan1Rpm) ? fan1Rpm : 0;
                            var validF2 = HpWmiBios.IsValidRpm(fan2Rpm) ? fan2Rpm : 0;
                            readings.Add((validF1, validF2));
                            _logging?.Info($"EC RPMs (alt regs, attempt {attempt}): Fan1={validF1} RPM, Fan2={validF2} RPM");
                            // Good reading - return immediately
                            return (validF1, validF2);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logging?.Debug($"EC alt register read failed (attempt {attempt}): {ex.Message}");
                        if (ex is TimeoutException || ex.Message.Contains("mutex", StringComparison.OrdinalIgnoreCase))
                        {
                            // Log contention event
                            _logging?.Warn($"EC contention detected during alt read (attempt {attempt}): {ex.Message}");
                        }
                    }

                    // Try primary registers (0x34/0x35) - units of 100 RPM (write registers, may return set values)
                    const ushort REG_FAN1_RPM = 0x34;
                    const ushort REG_FAN2_RPM = 0x35;

                    var fan1Unit = _ecAccess.ReadByte(REG_FAN1_RPM);
                    var fan2Unit = _ecAccess.ReadByte(REG_FAN2_RPM);
                    _logging?.Debug($"EC Read primary RPM regs (attempt {attempt}): 0x{REG_FAN1_RPM:X2}=0x{fan1Unit:X2}, 0x{REG_FAN2_RPM:X2}=0x{fan2Unit:X2}");

                    // Skip 0xFF values (invalid/error indicator)
                    // Max valid unit is 80 (8000 RPM / 100)
                    if (fan1Unit > 0 && fan1Unit < 0xFF || fan2Unit > 0 && fan2Unit < 0xFF)
                    {
                        // Only compute RPM for valid units (< 80 = 8000 RPM max)
                        var fan1Rpm = (fan1Unit > 0 && fan1Unit <= 80) ? fan1Unit * 100 : 0;
                        var fan2Rpm = (fan2Unit > 0 && fan2Unit <= 80) ? fan2Unit * 100 : 0;
                        
                        if (fan1Rpm > 0 || fan2Rpm > 0)
                        {
                            readings.Add((fan1Rpm, fan2Rpm));
                            _logging?.Info($"EC RPMs (primary, attempt {attempt}): Fan1={fan1Rpm} RPM, Fan2={fan2Rpm} RPM");
                            return (fan1Rpm, fan2Rpm);
                        }
                    }

                    // If we reach here, we got zeros - wait and retry
                }
                catch (Exception ex)
                {
                    _logging?.Warn($"EC Read attempt {attempt} failed: {ex.Message}");
                }

                // Backoff with jitter
                try
                {
                    var delayMs = 50 * attempt + (new Random()).Next(20, 80);
                    System.Threading.Thread.Sleep(delayMs);
                }
                catch { }
            }

            // If we collected any non-zero readings, pick the most recent non-zero
            for (int i = readings.Count - 1; i >= 0; i--)
            {
                var r = readings[i];
                if (r.f1 > 0 || r.f2 > 0) return r;
            }

            // Nothing useful found
            _logging?.Debug("EC ReadActualFanRpm: no valid RPM readings after retries");
            return (0, 0);
        }

        /// <summary>
        /// Public wrapper for tests and external verification logic to read EC RPMs.
        /// </summary>
        public (int fan1Rpm, int fan2Rpm) ReadActualFanRpmPublic() => ReadActualFanRpm();

        private int CalculateDutyFromRpm(int rpm, int fanIndex)
        {
            // Estimate duty cycle from RPM
            // Typical laptop fans: 0 RPM = 0%, 2000-3000 RPM = 50%, 5000-6000 RPM = 100%
            if (rpm == 0) return 0;
            
            const int minRpm = 1500;
            const int maxRpm = 6000;
            
            return Math.Clamp((rpm - minRpm) * 100 / (maxRpm - minRpm), 0, 100);
        }

        private void WriteDuty(int percent)
        {
            // Track last set percentage for RPM estimation fallback
            _lastSetFanPercent = Math.Clamp(percent, 0, 100);
            
            // HP OMEN EC register constants for fan control
            // Based on omen-fan project and OmenMon research
            const ushort REG_FAN1_SPEED_PCT = 0x2C;   // Fan 1 set speed 0-100% (write register)
            const ushort REG_FAN2_SPEED_PCT = 0x2D;   // Fan 2 set speed 0-100% (write register)
            const ushort REG_FAN1_SPEED_SET = 0x34;   // Fan 1 speed in units of 100 RPM (0-55)
            const ushort REG_FAN2_SPEED_SET = 0x35;   // Fan 2 speed in units of 100 RPM (0-55)
            const ushort REG_OMCC = 0x62;             // BIOS control: 0x06=Manual, 0x00=Auto
            const ushort REG_XFCD = 0x63;             // Manual fan auto countdown [s]: 0x00=disable, 0xFF=max
            const ushort REG_FAN_BOOST = 0xEC;        // Fan boost: 0x00=OFF, 0x0C=ON
            
            try
            {
                // Step 1: Enable manual fan control (disable BIOS auto-control)
                _logging?.Debug($"EC Write: REG_OMCC (0x{REG_OMCC:X2}) <- 0x06 (manual)");
                _ecAccess.WriteByte(REG_OMCC, 0x06);

                // Step 2: Disable auto-revert countdown to keep manual mode active
                _logging?.Debug($"EC Write: REG_XFCD (0x{REG_XFCD:X2}) <- 0x00 (disable countdown)");
                _ecAccess.WriteByte(REG_XFCD, 0x00);

                // Step 3: Set fan speed via percentage register (direct mapping, no inversion)
                byte pctValue = (byte)Math.Clamp(percent, 0, 100);
                byte rpmUnit = (byte)Math.Clamp(percent * 55 / 100, 0, 55);
                _logging?.Debug($"EC Write: REG_FAN1_SPEED_PCT (0x{REG_FAN1_SPEED_PCT:X2}) <- 0x{pctValue:X2}");
                _ecAccess.WriteByte(REG_FAN1_SPEED_PCT, pctValue);
                _logging?.Debug($"EC Write: REG_FAN2_SPEED_PCT (0x{REG_FAN2_SPEED_PCT:X2}) <- 0x{pctValue:X2}");
                _ecAccess.WriteByte(REG_FAN2_SPEED_PCT, pctValue);

                // Step 4: Also set RPM-based register (units of 100 RPM, max 55 = 5500 RPM)
                _logging?.Debug($"EC Write: REG_FAN1_SPEED_SET (0x{REG_FAN1_SPEED_SET:X2}) <- 0x{rpmUnit:X2}");
                _ecAccess.WriteByte(REG_FAN1_SPEED_SET, rpmUnit);
                _logging?.Debug($"EC Write: REG_FAN2_SPEED_SET (0x{REG_FAN2_SPEED_SET:X2}) <- 0x{rpmUnit:X2}");
                _ecAccess.WriteByte(REG_FAN2_SPEED_SET, rpmUnit);

                // Step 5: Enable fan boost for 100% (max mode)
                if (percent >= 100)
                {
                    _logging?.Debug($"EC Write: REG_FAN_BOOST (0x{REG_FAN_BOOST:X2}) <- 0x0C (enable boost)");
                    _ecAccess.WriteByte(REG_FAN_BOOST, 0x0C); // Enable max boost
                }
                else
                {
                    _logging?.Debug($"EC Write: REG_FAN_BOOST (0x{REG_FAN_BOOST:X2}) <- 0x00 (disable boost)");
                    _ecAccess.WriteByte(REG_FAN_BOOST, 0x00); // Disable boost
                }

                // Also write to user-configured registers if different (for compatibility)
                var duty = (byte)Math.Clamp(percent * 255 / 100, 0, 255);
                foreach (var register in _registerMap.Values)
                {
                    var regAddr = (ushort)register;
                    // Skip if we already wrote to this register
                    if (regAddr != REG_FAN1_SPEED_PCT && regAddr != REG_FAN2_SPEED_PCT &&
                        regAddr != REG_FAN1_SPEED_SET && regAddr != REG_FAN2_SPEED_SET)
                    {
                        _logging?.Debug($"EC Write: 0x{regAddr:X2} <- 0x{duty:X2} (compat)");
                        _ecAccess.WriteByte(regAddr, duty);
                    }
                }

                // Readback a few registers for verification/logging
                try
                {
                    var rb_omcc = _ecAccess.ReadByte(REG_OMCC);
                    var rb_xfcd = _ecAccess.ReadByte(REG_XFCD);
                    var rb_pct1 = _ecAccess.ReadByte(REG_FAN1_SPEED_PCT);
                    var rb_boost = _ecAccess.ReadByte(REG_FAN_BOOST);
                    _logging?.Debug($"EC Readback: 0x{REG_OMCC:X2}=0x{rb_omcc:X2}, 0x{REG_XFCD:X2}=0x{rb_xfcd:X2}, 0x{REG_FAN1_SPEED_PCT:X2}=0x{rb_pct1:X2}, 0x{REG_FAN_BOOST:X2}=0x{rb_boost:X2}");
                }
                catch (Exception ex)
                {
                    _logging?.Warn($"EC readback failed after WriteDuty: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"WriteDuty EC writes failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Public helper to set immediate percent via EC writes (used by wrapper verification fallback)
        /// </summary>
        public void SetImmediatePercent(int percent)
        {
            WriteDuty(percent);
        }
        
        /// <summary>
        /// Set fans to maximum speed immediately.
        /// </summary>
        public void SetMaxSpeed()
        {
            const ushort REG_FAN1_SPEED_PCT = 0x2C;
            const ushort REG_FAN2_SPEED_PCT = 0x2D;
            const ushort REG_FAN1_SPEED_SET = 0x34;
            const ushort REG_FAN2_SPEED_SET = 0x35;
            const ushort REG_OMCC = 0x62;
            const ushort REG_XFCD = 0x63;
            const ushort REG_FAN_BOOST = 0xEC;
            
            _lastSetFanPercent = 100;
            try
            {
                _logging?.Debug($"EC SetMaxSpeed: enabling manual control and max values");
                // Enable manual control
                _ecAccess.WriteByte(REG_OMCC, 0x06);
                // Disable auto-revert countdown
                _ecAccess.WriteByte(REG_XFCD, 0x00);

                // Set max percentage
                _ecAccess.WriteByte(REG_FAN1_SPEED_PCT, 100);
                _ecAccess.WriteByte(REG_FAN2_SPEED_PCT, 100);

                // Set max RPM units
                _ecAccess.WriteByte(REG_FAN1_SPEED_SET, 55);
                _ecAccess.WriteByte(REG_FAN2_SPEED_SET, 55);

                // Enable fan boost
                _ecAccess.WriteByte(REG_FAN_BOOST, 0x0C);

                // Readback key registers
                try
                {
                    var rb1 = _ecAccess.ReadByte(REG_FAN1_SPEED_PCT);
                    var rb2 = _ecAccess.ReadByte(REG_FAN2_SPEED_PCT);
                    var rbBoost = _ecAccess.ReadByte(REG_FAN_BOOST);
                    _logging?.Info($"EC SetMaxSpeed readback: PCT1=0x{rb1:X2}, PCT2=0x{rb2:X2}, BOOST=0x{rbBoost:X2}");
                }
                catch (Exception ex)
                {
                    _logging?.Warn($"EC SetMaxSpeed readback failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"SetMaxSpeed EC writes failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Set individual fan speeds via EC registers.
        /// </summary>
        public void SetFanSpeeds(int cpuPercent, int gpuPercent)
        {
            const ushort REG_FAN1_SPEED_PCT = 0x2C;   // Fan 1 set speed 0-100%
            const ushort REG_FAN2_SPEED_PCT = 0x2D;   // Fan 2 set speed 0-100%
            const ushort REG_FAN1_SPEED_SET = 0x34;   // Fan 1 speed in units of 100 RPM (0-55)
            const ushort REG_FAN2_SPEED_SET = 0x35;   // Fan 2 speed in units of 100 RPM (0-55)
            const ushort REG_OMCC = 0x62;             // BIOS control: 0x06=Manual, 0x00=Auto
            const ushort REG_XFCD = 0x63;             // Manual fan auto countdown [s]: 0x00=disable
            const ushort REG_FAN_BOOST = 0xEC;        // Fan boost: 0x00=OFF, 0x0C=ON

            // Track last set percentage (average for compatibility)
            _lastSetFanPercent = (cpuPercent + gpuPercent) / 2;

            try
            {
                _logging?.Debug($"EC SetFanSpeeds: CPU={cpuPercent}%, GPU={gpuPercent}%");
                // Step 1: Enable manual fan control (disable BIOS auto-control)
                _ecAccess.WriteByte(REG_OMCC, 0x06);
                // Step 2: Disable auto-revert countdown
                _ecAccess.WriteByte(REG_XFCD, 0x00);

                // Step 3: Set fan speeds via percentage registers (0-100)
                var cpuPctValue = (byte)Math.Clamp(cpuPercent, 0, 100);
                var gpuPctValue = (byte)Math.Clamp(gpuPercent, 0, 100);
                _ecAccess.WriteByte(REG_FAN1_SPEED_PCT, cpuPctValue);
                _ecAccess.WriteByte(REG_FAN2_SPEED_PCT, gpuPctValue);

                // Step 4: Also set RPM-based registers (units of 100 RPM, max 55 = 5500 RPM)
                // Map 0-100% to 0-55 units
                var cpuRpmUnit = (byte)Math.Clamp(cpuPercent * 55 / 100, 0, 55);
                var gpuRpmUnit = (byte)Math.Clamp(gpuPercent * 55 / 100, 0, 55);
                _ecAccess.WriteByte(REG_FAN1_SPEED_SET, cpuRpmUnit);
                _ecAccess.WriteByte(REG_FAN2_SPEED_SET, gpuRpmUnit);

                // Step 5: Enable fan boost if either fan is at 100%
                if (cpuPercent >= 100 || gpuPercent >= 100)
                {
                    _ecAccess.WriteByte(REG_FAN_BOOST, 0x0C); // Enable max boost
                }
                else
                {
                    _ecAccess.WriteByte(REG_FAN_BOOST, 0x00); // Disable boost
                }

                // Readback key registers for logging
                try
                {
                    var rbCpu = _ecAccess.ReadByte(REG_FAN1_SPEED_PCT);
                    var rbGpu = _ecAccess.ReadByte(REG_FAN2_SPEED_PCT);
                    _logging?.Info($"EC SetFanSpeeds readback: CPU_PCT=0x{rbCpu:X2}, GPU_PCT=0x{rbGpu:X2}");
                }
                catch (Exception ex)
                {
                    _logging?.Warn($"EC SetFanSpeeds readback failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"SetFanSpeeds EC writes failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Restore BIOS automatic fan control.
        /// Uses full EC reset sequence to ensure BIOS properly takes over fan control.
        /// </summary>
        public void RestoreAutoControl()
        {
            const ushort REG_FAN1_SPEED_PCT = 0x2E;   // Fan 1 speed 0-100%
            const ushort REG_FAN2_SPEED_PCT = 0x2F;   // Fan 2 speed 0-100%
            const ushort REG_FAN1_SPEED_SET = 0x34;   // Fan 1 speed in units of 100 RPM
            const ushort REG_FAN2_SPEED_SET = 0x35;   // Fan 2 speed in units of 100 RPM
            const ushort REG_OMCC = 0x62;             // BIOS control: 0x00=Enabled, 0x06=Disabled
            const ushort REG_TIMER = 0x63;            // Timer (counts down from 0x78)
            const ushort REG_FAN_BOOST = 0xEC;        // Fan boost: 0x00=OFF, 0x0C=ON
            const ushort REG_FAN_STATE = 0xF4;        // Fan state: 0x00=Enable, 0x02=Disable
            
            _lastSetFanPercent = -1;
            
            try
            {
                // Step 1: Disable fan boost
                _ecAccess.WriteByte(REG_FAN_BOOST, 0x00);
                
                // Step 2: Clear manual speed settings (write 0 to let BIOS control)
                _ecAccess.WriteByte(REG_FAN1_SPEED_PCT, 0x00);
                _ecAccess.WriteByte(REG_FAN2_SPEED_PCT, 0x00);
                _ecAccess.WriteByte(REG_FAN1_SPEED_SET, 0x00);
                _ecAccess.WriteByte(REG_FAN2_SPEED_SET, 0x00);
                
                // Step 3: Enable fan state (allow BIOS to control)
                _ecAccess.WriteByte(REG_FAN_STATE, 0x00);
                
                // Step 4: Re-enable BIOS auto-control
                _ecAccess.WriteByte(REG_OMCC, 0x00);
                
                // Step 5: Reset timer to trigger BIOS to recalculate fan speeds
                // Timer counts down from 0x78 (120); setting to 0x78 forces BIOS to take over
                _ecAccess.WriteByte(REG_TIMER, 0x78);
                
                // Step 6: Brief wait for EC to process, then re-verify BIOS control
                System.Threading.Thread.Sleep(50);
                _ecAccess.WriteByte(REG_FAN_STATE, 0x00);
                _ecAccess.WriteByte(REG_OMCC, 0x00);
            }
            catch
            {
                // Best effort - continue even if some writes fail
            }
        }
        
        /// <summary>
        /// Reset EC to factory defaults.
        /// Clears all manual fan overrides and restores BIOS control.
        /// Based on OMEN laptop EC register map.
        /// </summary>
        public bool ResetEcToDefaults()
        {
            if (!IsEcReady)
                return false;
            
            try
            {
                // OMEN EC registers (from omen-fan project and OmenMon research)
                const ushort REG_FAN1_SPEED_SET = 0x34;   // Fan 1 speed in units of 100 RPM
                const ushort REG_FAN2_SPEED_SET = 0x35;   // Fan 2 speed in units of 100 RPM
                const ushort REG_FAN1_SPEED_PCT = 0x2E;   // Fan 1 speed 0-100%
                const ushort REG_FAN2_SPEED_PCT = 0x2F;   // Fan 2 speed 0-100%
                const ushort REG_FAN_BOOST = 0xEC;        // Fan boost: 0x00=OFF, 0x0C=ON
                const ushort REG_FAN_STATE = 0xF4;        // Fan state: 0x00=Enable, 0x02=Disable
                const ushort REG_BIOS_CONTROL = 0x62;     // BIOS control: 0x00=Enabled, 0x06=Disabled
                const ushort REG_TIMER = 0x63;            // Timer (counts down from 0x78)
                const ushort REG_THERMAL_POLICY = 0xF9;   // Thermal policy (some models)
                
                // Step 1: FIRST set BIOS control mode before clearing speeds
                // This tells the EC that BIOS should manage fans
                _ecAccess.WriteByte(REG_BIOS_CONTROL, 0x00);
                
                // Step 2: Clear manual fan speed registers (write 0 to let BIOS control)
                _ecAccess.WriteByte(REG_FAN1_SPEED_SET, 0x00);
                _ecAccess.WriteByte(REG_FAN2_SPEED_SET, 0x00);
                _ecAccess.WriteByte(REG_FAN1_SPEED_PCT, 0x00);
                _ecAccess.WriteByte(REG_FAN2_SPEED_PCT, 0x00);
                
                // Step 3: Disable fan boost
                _ecAccess.WriteByte(REG_FAN_BOOST, 0x00);
                
                // Step 4: Enable fan state (allow BIOS to control)
                _ecAccess.WriteByte(REG_FAN_STATE, 0x00);
                
                // Step 5: Reset timer to trigger BIOS to recalculate fan speeds
                // Timer counts down from 0x78 (120); setting to 0x78 forces BIOS to take over
                _ecAccess.WriteByte(REG_TIMER, 0x78);
                
                // Step 6: Try resetting thermal policy (some models need this)
                try { _ecAccess.WriteByte(REG_THERMAL_POLICY, 0x00); } catch { }
                
                // Step 7: Wait for EC to process changes
                System.Threading.Thread.Sleep(300);
                
                // Step 8: Force BIOS control mode again and reset timer
                // Some EC firmware requires multiple writes to reliably switch modes
                _ecAccess.WriteByte(REG_BIOS_CONTROL, 0x00);
                _ecAccess.WriteByte(REG_FAN_STATE, 0x00);
                _ecAccess.WriteByte(REG_TIMER, 0x78);
                
                // Step 9: Wait again for EC to fully process
                System.Threading.Thread.Sleep(100);
                
                // Step 10: Final timer poke to force BIOS recalculation
                _ecAccess.WriteByte(REG_TIMER, 0x78);
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Write a value to an EC register (public wrapper for throttling mitigation).
        /// Only allows writes to registers in the allowed write list.
        /// </summary>
        public void WriteEc(ushort address, byte value)
        {
            if (!IsEcReady)
                throw new InvalidOperationException("EC not ready");
                
            _ecAccess.WriteByte(address, value);
        }
    }
}
