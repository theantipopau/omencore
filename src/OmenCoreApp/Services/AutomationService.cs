using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Evaluates and executes automation rules based on system conditions.
    /// Runs background worker that checks all active rules every 5 seconds.
    /// </summary>
    public class AutomationService : IDisposable
    {
        private readonly LoggingService _logger;
        private readonly ConfigurationService _configService;
        private readonly FanService _fanService;
        private readonly ThermalSensorProvider? _thermalProvider;
        private readonly NvapiService? _nvapiService;
        private readonly UndervoltService? _undervoltService;
        private readonly ProcessMonitoringService _processMonitor;
        
        private readonly System.Threading.Timer _evaluationTimer;
        private readonly HashSet<string> _activeRules = new();
        private readonly object _lock = new();
        private bool _isRunning;
        private DateTime _lastIdleCheck = DateTime.Now;

        /// <summary>
        /// Fired when a rule is triggered
        /// </summary>
        public event EventHandler<RuleTriggeredEventArgs>? RuleTriggered;

        public AutomationService(
            LoggingService logger,
            ConfigurationService configService,
            FanService fanService,
            ProcessMonitoringService processMonitor,
            ThermalSensorProvider? thermalProvider = null,
            NvapiService? nvapiService = null,
            UndervoltService? undervoltService = null)
        {
            _logger = logger;
            _configService = configService;
            _fanService = fanService;
            _thermalProvider = thermalProvider;
            _nvapiService = nvapiService;
            _undervoltService = undervoltService;
            _processMonitor = processMonitor;
            
            _evaluationTimer = new System.Threading.Timer(EvaluateRules, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Start automation service
        /// </summary>
        public void Start()
        {
            if (_isRunning)
                return;

            _isRunning = true;
            _evaluationTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5)); // Evaluate every 5 seconds
            _logger.Info("Automation service started (5s interval)");
        }

        /// <summary>
        /// Stop automation service
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _evaluationTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _logger.Info("Automation service stopped");
        }

        private void EvaluateRules(object? state)
        {
            if (!_isRunning)
                return;

            try
            {
                var config = _configService.Config;
                var enabledRules = config.AutomationRules
                    .Where(r => r.Enabled)
                    .OrderBy(r => r.Priority) // Lower priority number = higher priority
                    .ToList();

                foreach (var rule in enabledRules)
                {
                    try
                    {
                        var shouldTrigger = EvaluateTrigger(rule);
                        var ruleKey = rule.Id;

                        lock (_lock)
                        {
                            var wasActive = _activeRules.Contains(ruleKey);

                            if (shouldTrigger && !wasActive)
                            {
                                // Rule just became active - execute actions
                                _activeRules.Add(ruleKey);
                                ExecuteActions(rule);
                                
                                // Update statistics
                                rule.LastTriggeredAt = DateTime.Now;
                                rule.TriggerCount++;
                                _configService.Save(config);

                                RuleTriggered?.Invoke(this, new RuleTriggeredEventArgs(rule));
                                
                                _logger.Info($"Automation rule triggered: {rule.Name}");
                            }
                            else if (!shouldTrigger && wasActive)
                            {
                                // Rule is no longer active
                                _activeRules.Remove(ruleKey);
                                _logger.Debug($"Automation rule deactivated: {rule.Name}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error evaluating automation rule {rule.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in automation evaluation cycle: {ex.Message}");
            }
        }

        private bool EvaluateTrigger(AutomationRule rule)
        {
            return rule.Trigger switch
            {
                TriggerType.Time => EvaluateTimeTrigger(rule.TriggerData),
                TriggerType.Battery => EvaluateBatteryTrigger(rule.TriggerData),
                TriggerType.ACPower => EvaluateACPowerTrigger(rule.TriggerData),
                TriggerType.Temperature => EvaluateTemperatureTrigger(rule.TriggerData),
                TriggerType.Process => EvaluateProcessTrigger(rule.TriggerData),
                TriggerType.Idle => EvaluateIdleTrigger(rule.TriggerData),
                TriggerType.WiFiSSID => EvaluateWiFiTrigger(rule.TriggerData),
                _ => false
            };
        }

        private bool EvaluateTimeTrigger(TriggerConfig config)
        {
            if (!config.StartTime.HasValue || !config.EndTime.HasValue)
                return false;

            var now = DateTime.Now;
            var currentTime = now.TimeOfDay;

            // Check day of week filter
            if (config.Days != null && config.Days.Count > 0)
            {
                if (!config.Days.Contains(now.DayOfWeek))
                    return false;
            }

            var start = config.StartTime.Value;
            var end = config.EndTime.Value;

            // Handle time ranges that cross midnight
            if (end < start)
            {
                return currentTime >= start || currentTime <= end;
            }
            else
            {
                return currentTime >= start && currentTime <= end;
            }
        }

        private bool EvaluateBatteryTrigger(TriggerConfig config)
        {
            if (!config.BatteryThreshold.HasValue)
                return false;

            try
            {
                var battery = SystemInformation.PowerStatus.BatteryLifePercent * 100;
                var threshold = config.BatteryThreshold.Value;

                return config.BatteryCondition?.ToLowerInvariant() switch
                {
                    "below" => battery < threshold,
                    "above" => battery > threshold,
                    _ => false
                };
            }
            catch
            {
                return false;
            }
        }

        private bool EvaluateACPowerTrigger(TriggerConfig config)
        {
            if (!config.ACConnected.HasValue)
                return false;

            try
            {
                var isACConnected = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
                return isACConnected == config.ACConnected.Value;
            }
            catch
            {
                return false;
            }
        }

        private bool EvaluateTemperatureTrigger(TriggerConfig config)
        {
            if (!config.TemperatureThreshold.HasValue || _thermalProvider == null)
                return false;

            try
            {
                var temps = _thermalProvider.ReadTemperatures();
                var sensor = config.TemperatureSensor?.ToLowerInvariant() ?? "cpu";
                
                var reading = temps.FirstOrDefault(t => 
                    t.Sensor.ToLowerInvariant().Contains(sensor));

                if (reading == null || string.IsNullOrEmpty(reading.Sensor) || reading.Celsius <= 0)
                    return false;

                var threshold = config.TemperatureThreshold.Value;

                return config.TemperatureCondition?.ToLowerInvariant() switch
                {
                    "above" => reading.Celsius > threshold,
                    "below" => reading.Celsius < threshold,
                    _ => false
                };
            }
            catch
            {
                return false;
            }
        }

        private bool EvaluateProcessTrigger(TriggerConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.ProcessName))
                return false;

            var activeProcesses = _processMonitor.ActiveProcesses.Values
                .Select(p => p.ProcessName.ToLowerInvariant())
                .ToList();

            var targetProcess = config.ProcessName.ToLowerInvariant();
            return activeProcesses.Any(p => p.Contains(targetProcess) || targetProcess.Contains(p));
        }

        private bool EvaluateIdleTrigger(TriggerConfig config)
        {
            if (!config.IdleMinutes.HasValue)
                return false;

            try
            {
                // Get last input time from Windows
                var lastInputInfo = new LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                if (!GetLastInputInfo(ref lastInputInfo))
                    return false;

                var idleTime = TimeSpan.FromMilliseconds(Environment.TickCount - lastInputInfo.dwTime);
                return idleTime.TotalMinutes >= config.IdleMinutes.Value;
            }
            catch
            {
                return false;
            }
        }

        private bool EvaluateWiFiTrigger(TriggerConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.WiFiSSID))
                return false;

            try
            {
                // Query WMI for connected WiFi networks
                using var searcher = new ManagementObjectSearcher(
                    "root\\WlanApi",
                    "SELECT * FROM MSNdis_80211_ServiceSetIdentifier");

                foreach (ManagementObject obj in searcher.Get())
                {
                    var ssidBytes = obj["Ndis80211SsId"] as byte[];
                    if (ssidBytes != null)
                    {
                        var ssid = System.Text.Encoding.UTF8.GetString(ssidBytes).Trim('\0');
                        if (string.Equals(ssid, config.WiFiSSID, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch
            {
                // WMI query failed or no WiFi - fall back to simple check
                try
                {
                    var activeConnections = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                   ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
                    
                    // This is a simplified check - actual SSID retrieval requires native WiFi API
                    return activeConnections.Any();
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private void ExecuteActions(AutomationRule rule)
        {
            foreach (var action in rule.Actions)
            {
                try
                {
                    ExecuteAction(action);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error executing action {action.Type} for rule {rule.Name}: {ex.Message}");
                }
            }
        }

        private void ExecuteAction(RuleAction action)
        {
            switch (action.Type)
            {
                case ActionType.SetFanPreset:
                    if (!string.IsNullOrWhiteSpace(action.Parameter))
                    {
                        var config = _configService.Config;
                        var preset = config.FanPresets?.FirstOrDefault(p =>
                            string.Equals(p.Name, action.Parameter, StringComparison.OrdinalIgnoreCase));
                        if (preset != null)
                        {
                            _fanService.ApplyPreset(preset);
                            _logger.Info($"Applied fan preset: {action.Parameter}");
                        }
                    }
                    break;

                case ActionType.SetPerformanceMode:
                    // Performance mode requires platform-specific implementation
                    _logger.Info($"Performance mode action: {action.Parameter} (not yet implemented)");
                    break;

                case ActionType.SetGpuOcProfile:
                    if (!string.IsNullOrWhiteSpace(action.Parameter) && _nvapiService != null)
                    {
                        var config = _configService.Config;
                        var ocProfile = config.GpuOcProfiles?.FirstOrDefault(p =>
                            string.Equals(p.Name, action.Parameter, StringComparison.OrdinalIgnoreCase));
                        if (ocProfile != null)
                        {
                            _nvapiService.SetCoreClockOffset(ocProfile.CoreClockOffsetMHz);
                            _nvapiService.SetMemoryClockOffset(ocProfile.MemoryClockOffsetMHz);
                            _nvapiService.SetPowerLimit(ocProfile.PowerLimitPercent);
                            _logger.Info($"Applied GPU OC profile: {action.Parameter}");
                        }
                    }
                    break;

                case ActionType.SetAmdStapmLimit:
                    if (action.NumericParameter.HasValue && _undervoltService?.Provider is AmdUndervoltProvider amdProvider)
                    {
                        amdProvider.SetStapmLimit((uint)action.NumericParameter.Value * 1000); // W to mW
                        _logger.Info($"Applied AMD STAPM limit: {action.NumericParameter.Value}W");
                    }
                    break;

                case ActionType.SetAmdTempLimit:
                    if (action.NumericParameter.HasValue && _undervoltService?.Provider is AmdUndervoltProvider amdProvider2)
                    {
                        amdProvider2.SetTctlTemp((uint)action.NumericParameter.Value);
                        _logger.Info($"Applied AMD temp limit: {action.NumericParameter.Value}°C");
                    }
                    break;

                case ActionType.ShowNotification:
                    if (!string.IsNullOrWhiteSpace(action.Parameter))
                    {
                        // Would integrate with notification service if available
                        _logger.Info($"Notification: {action.Parameter}");
                    }
                    break;
            }
        }

        // P/Invoke for idle time detection
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        public void Dispose()
        {
            Stop();
            _evaluationTimer.Dispose();
        }
    }

    /// <summary>
    /// Event args for rule triggered event
    /// </summary>
    public class RuleTriggeredEventArgs : EventArgs
    {
        public AutomationRule Rule { get; }

        public RuleTriggeredEventArgs(AutomationRule rule)
        {
            Rule = rule;
        }
    }
}
