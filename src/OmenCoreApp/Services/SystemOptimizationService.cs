using System;
using System.Collections.Generic;
using System.Diagnostics;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class SystemOptimizationService
    {
        private readonly LoggingService _logging;

        public SystemOptimizationService(LoggingService logging)
        {
            _logging = logging;
        }

        public void ApplyToggle(ServiceToggle toggle, bool enable)
        {
            try
            {
                var verb = enable ? "start" : "stop";
                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"{verb} {toggle.ServiceName}",
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                _logging.Info($"{verb.ToUpperInvariant()} service {toggle.ServiceName}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to toggle {toggle.ServiceName}", ex);
            }
        }

        public void ApplyWindowsAnimations(bool enable)
        {
            var value = enable ? 0 : 2;
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"Set-ItemProperty -Path 'HKCU:Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects' -Name 'VisualFXSetting' -Value {value}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
            _logging.Info(enable ? "Enabled animations" : "Disabled animations");
        }

        public void ApplySchedulerTweak(bool enable)
        {
            var cmd = enable ? "bcdedit /set disabledynamictick yes" : "bcdedit /deletevalue disabledynamictick";
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"Start-Process cmd -ArgumentList '/c {cmd}' -Verb runAs\"",
                UseShellExecute = false
            });
            _logging.Info(enable ? "Scheduler tweaks enabled" : "Scheduler tweaks restored");
        }

        public void ApplyGamingMode(IEnumerable<ServiceToggle> toggles)
        {
            ApplyWindowsAnimations(false);
            ApplySchedulerTweak(true);
            foreach (var toggle in toggles)
            {
                ApplyToggle(toggle, toggle.EnabledByDefault);
            }
            _logging.Info("Gaming Mode preset applied");
        }

        public void RestoreDefaults()
        {
            ApplyWindowsAnimations(true);
            ApplySchedulerTweak(false);
            _logging.Info("System tweaks restored");
        }
    }
}
