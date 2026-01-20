using System;
using System.Collections.Generic;
using System.Linq;
using OmenCore.Models;

namespace OmenCore.Hardware
{
    public class FanController
    {
        private readonly IEcAccess _ecAccess;
        private readonly IReadOnlyDictionary<string, int> _registerMap;
        private readonly LibreHardwareMonitorImpl _bridge;
        
        // EC registers for reading actual fan RPM (from omen-fan project)
        private const ushort REG_FAN1_RPM = 0x34;  // Fan 1 speed in units of 100 RPM
        private const ushort REG_FAN2_RPM = 0x35;  // Fan 2 speed in units of 100 RPM
        
        // Track last set fan percentage for fallback estimation only
        private int _lastSetFanPercent = -1;

        public FanController(IEcAccess ecAccess, IReadOnlyDictionary<string, int> registerMap, LibreHardwareMonitorImpl bridge)
        {
            _ecAccess = ecAccess;
            _registerMap = registerMap;
            _bridge = bridge;
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
        /// </summary>
        private (int fan1Rpm, int fan2Rpm) ReadActualFanRpm()
        {
            if (!_ecAccess.IsAvailable)
                return (0, 0);

            try
            {
                // Try primary registers (0x34/0x35) - units of 100 RPM
                var fan1Unit = _ecAccess.ReadByte(REG_FAN1_RPM);
                var fan2Unit = _ecAccess.ReadByte(REG_FAN2_RPM);
                
                // DEBUG: Log what we read from primary registers
                System.Diagnostics.Debug.WriteLine($"[FanController] Primary registers - Fan1: {fan1Unit}, Fan2: {fan2Unit}");

                if (fan1Unit > 0 || fan2Unit > 0)
                {
                    // Convert from 100 RPM units to actual RPM
                    var fan1Rpm = fan1Unit * 100;
                    var fan2Rpm = fan2Unit * 100;
                    System.Diagnostics.Debug.WriteLine($"[FanController] Using primary registers - Fan1RPM: {fan1Rpm}, Fan2RPM: {fan2Rpm}");
                    return (fan1Rpm, fan2Rpm);
                }

                // Try alternative registers (0x4A-0x4B for Fan1, 0x4C-0x4D for Fan2) - 16-bit RPM
                try
                {
                    var fan1Low = _ecAccess.ReadByte(0x4A);
                    var fan1High = _ecAccess.ReadByte(0x4B);
                    var fan2Low = _ecAccess.ReadByte(0x4C);
                    var fan2High = _ecAccess.ReadByte(0x4D);
                    
                    System.Diagnostics.Debug.WriteLine($"[FanController] Alt registers - F1L:{fan1Low} F1H:{fan1High} F2L:{fan2Low} F2H:{fan2High}");

                    var fan1Rpm = (fan1High << 8) | fan1Low;
                    var fan2Rpm = (fan2High << 8) | fan2Low;

                    if (fan1Rpm > 0 || fan2Rpm > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FanController] Using alt registers - Fan1RPM: {fan1Rpm}, Fan2RPM: {fan2Rpm}");
                        return (fan1Rpm, fan2Rpm);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FanController] Alt register read failed: {ex.Message}");
                    // Alternative registers not available
                }

                // Try OMEN 17-ck2xxx specific registers (placeholder - research needed)
                // TODO: Research actual EC registers for OMEN 17-ck2xxx fan RPM
                // Example: Some OMEN models use 0x60/0x61 for CPU fan, 0x62/0x63 for GPU fan
                try
                {
                    var fan1RpmAlt = _ecAccess.ReadByte(0x60) | (_ecAccess.ReadByte(0x61) << 8);
                    var fan2RpmAlt = _ecAccess.ReadByte(0x62) | (_ecAccess.ReadByte(0x63) << 8);
                    System.Diagnostics.Debug.WriteLine($"[FanController] Model-specific registers - Fan1RPM: {fan1RpmAlt}, Fan2RPM: {fan2RpmAlt}");


                    if (fan1RpmAlt > 0 || fan2RpmAlt > 0)
                    {
                        return (fan1RpmAlt, fan2RpmAlt);
                    }
                }
                catch
                {
                    // Model-specific registers not available
                }

                System.Diagnostics.Debug.WriteLine($"[FanController] All register attempts failed, returning (0, 0)");
                return (0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FanController] ReadActualFanRpm exception: {ex.Message}");
                return (0, 0);
            }
        }

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
            const ushort REG_FAN1_SPEED_PCT = 0x2E;   // Fan 1 speed 0-100%
            const ushort REG_FAN2_SPEED_PCT = 0x2F;   // Fan 2 speed 0-100%
            const ushort REG_FAN1_SPEED_SET = 0x34;   // Fan 1 speed in units of 100 RPM (0-55)
            const ushort REG_FAN2_SPEED_SET = 0x35;   // Fan 2 speed in units of 100 RPM (0-55)
            const ushort REG_OMCC = 0x62;             // BIOS control: 0x06=Manual, 0x00=Auto
            const ushort REG_FAN_BOOST = 0xEC;        // Fan boost: 0x00=OFF, 0x0C=ON
            
            // Step 1: Enable manual fan control (disable BIOS auto-control)
            _ecAccess.WriteByte(REG_OMCC, 0x06);
            
            // Step 2: Set fan speed via percentage register (0-100)
            var pctValue = (byte)Math.Clamp(percent, 0, 100);
            _ecAccess.WriteByte(REG_FAN1_SPEED_PCT, pctValue);
            _ecAccess.WriteByte(REG_FAN2_SPEED_PCT, pctValue);
            
            // Step 3: Also set RPM-based register (units of 100 RPM, max 55 = 5500 RPM)
            // Map 0-100% to 0-55 units
            var rpmUnit = (byte)Math.Clamp(percent * 55 / 100, 0, 55);
            _ecAccess.WriteByte(REG_FAN1_SPEED_SET, rpmUnit);
            _ecAccess.WriteByte(REG_FAN2_SPEED_SET, rpmUnit);
            
            // Step 4: Enable fan boost for 100% (max mode)
            if (percent >= 100)
            {
                _ecAccess.WriteByte(REG_FAN_BOOST, 0x0C); // Enable max boost
            }
            else
            {
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
                    _ecAccess.WriteByte(regAddr, duty);
                }
            }
        }
        
        /// <summary>
        /// Set fans to maximum speed immediately.
        /// </summary>
        public void SetMaxSpeed()
        {
            const ushort REG_FAN1_SPEED_PCT = 0x2E;
            const ushort REG_FAN2_SPEED_PCT = 0x2F;
            const ushort REG_FAN1_SPEED_SET = 0x34;
            const ushort REG_FAN2_SPEED_SET = 0x35;
            const ushort REG_OMCC = 0x62;
            const ushort REG_FAN_BOOST = 0xEC;
            
            _lastSetFanPercent = 100;
            
            // Enable manual control
            _ecAccess.WriteByte(REG_OMCC, 0x06);
            
            // Set max percentage
            _ecAccess.WriteByte(REG_FAN1_SPEED_PCT, 100);
            _ecAccess.WriteByte(REG_FAN2_SPEED_PCT, 100);
            
            // Set max RPM units (55 = 5500 RPM)
            _ecAccess.WriteByte(REG_FAN1_SPEED_SET, 55);
            _ecAccess.WriteByte(REG_FAN2_SPEED_SET, 55);
            
            // Enable fan boost
            _ecAccess.WriteByte(REG_FAN_BOOST, 0x0C);
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
    }
}
