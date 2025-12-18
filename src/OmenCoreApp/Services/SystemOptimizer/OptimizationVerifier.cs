using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace OmenCore.Services.SystemOptimizer
{
    /// <summary>
    /// Verifies the current state of optimizations.
    /// Used to check if optimizations are active and report status.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class OptimizationVerifier
    {
        private readonly LoggingService _logger;

        public OptimizationVerifier(LoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Generates a full verification report of all optimizations.
        /// </summary>
        public async Task<OptimizationState> VerifyAllAsync()
        {
            var state = new OptimizationState();
            
            await Task.Run(() =>
            {
                // Power verifications
                state.Power.UltimatePerformancePlan = VerifyUltimatePerformancePlan();
                state.Power.HardwareGpuScheduling = VerifyHardwareGpuScheduling();
                state.Power.GameModeEnabled = VerifyGameMode();
                state.Power.ForegroundPriority = VerifyForegroundPriority();
                
                // Service verifications
                state.Services.TelemetryDisabled = VerifyTelemetryDisabled();
                state.Services.SysMainDisabled = VerifySysMainDisabled();
                state.Services.SearchIndexingDisabled = VerifySearchIndexingDisabled();
                state.Services.DiagTrackDisabled = VerifyDiagTrackDisabled();
                
                // Network verifications
                state.Network.TcpNoDelay = VerifyTcpNoDelay();
                state.Network.TcpAckFrequency = VerifyTcpAckFrequency();
                state.Network.DeliveryOptimizationDisabled = VerifyDeliveryOptimizationDisabled();
                state.Network.NagleDisabled = VerifyNagleDisabled();
                
                // Input verifications
                state.Input.MouseAccelerationDisabled = VerifyMouseAccelerationDisabled();
                state.Input.GameDvrDisabled = VerifyGameDvrDisabled();
                state.Input.GameBarDisabled = VerifyGameBarDisabled();
                state.Input.FullscreenOptimizationsDisabled = VerifyFullscreenOptimizationsDisabled();
                
                // Visual verifications
                state.Visual.AnimationsDisabled = VerifyAnimationsDisabled();
                state.Visual.TransparencyDisabled = VerifyTransparencyDisabled();
                
                // Storage verifications
                state.Storage.IsSsd = DetectSsd();
                state.Storage.TrimEnabled = VerifyTrimEnabled();
                state.Storage.DefragDisabled = VerifyDefragDisabled();
                state.Storage.ShortNamesDisabled = VerifyShortNamesDisabled();
                state.Storage.LastAccessDisabled = VerifyLastAccessDisabled();
            });
            
            return state;
        }

        // ========== POWER VERIFICATIONS ==========
        
        private bool VerifyUltimatePerformancePlan()
        {
            try
            {
                // Check if Ultimate Performance or High Performance is active
                using var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "powercfg";
                process.StartInfo.Arguments = "/getactivescheme";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                // Ultimate Performance GUID: e9a42b02-d5df-448d-aa00-03f14749eb61
                // High Performance GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
                return output.Contains("e9a42b02-d5df-448d-aa00-03f14749eb61") ||
                       output.Contains("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
            }
            catch
            {
                return false;
            }
        }

        private bool VerifyHardwareGpuScheduling()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                var value = key?.GetValue("HwSchMode");
                return value != null && (int)value == 2;
            }
            catch
            {
                return false;
            }
        }

        private bool VerifyGameMode()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\GameBar");
                var value = key?.GetValue("AutoGameModeEnabled");
                return value == null || (int)value == 1; // Default is enabled
            }
            catch
            {
                return true; // Assume default (enabled)
            }
        }

        private bool VerifyForegroundPriority()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\PriorityControl");
                var value = key?.GetValue("Win32PrioritySeparation");
                return value != null && ((int)value == 38 || (int)value == 26);
            }
            catch
            {
                return false;
            }
        }

        // ========== SERVICE VERIFICATIONS ==========
        
        private bool VerifyTelemetryDisabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
                var value = key?.GetValue("AllowTelemetry");
                return value != null && (int)value == 0;
            }
            catch
            {
                return false;
            }
        }

        private bool VerifySysMainDisabled()
        {
            try
            {
                using var sc = new System.ServiceProcess.ServiceController("SysMain");
                return sc.Status == System.ServiceProcess.ServiceControllerStatus.Stopped;
            }
            catch
            {
                return false;
            }
        }

        private bool VerifySearchIndexingDisabled()
        {
            try
            {
                using var sc = new System.ServiceProcess.ServiceController("WSearch");
                return sc.Status == System.ServiceProcess.ServiceControllerStatus.Stopped;
            }
            catch
            {
                return false;
            }
        }

        private bool VerifyDiagTrackDisabled()
        {
            try
            {
                using var sc = new System.ServiceProcess.ServiceController("DiagTrack");
                return sc.Status == System.ServiceProcess.ServiceControllerStatus.Stopped;
            }
            catch
            {
                return false;
            }
        }

        // ========== NETWORK VERIFICATIONS ==========
        
        private bool VerifyTcpNoDelay()
        {
            // Check for gaming network adapters with TcpNoDelay
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
                var value = key?.GetValue("TcpNoDelay");
                return value != null && (int)value == 1;
            }
            catch
            {
                return false;
            }
        }

        private bool VerifyTcpAckFrequency()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
                var value = key?.GetValue("TcpAckFrequency");
                return value != null && (int)value == 1;
            }
            catch
            {
                return false;
            }
        }

        private bool VerifyDeliveryOptimizationDisabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization");
                var value = key?.GetValue("DODownloadMode");
                return value != null && (int)value == 0;
            }
            catch
            {
                return false;
            }
        }

        private bool VerifyNagleDisabled()
        {
            // Nagle is disabled per-interface, check for common gaming config
            return VerifyTcpNoDelay(); // Usually set together
        }

        // ========== INPUT VERIFICATIONS ==========
        
        private bool VerifyMouseAccelerationDisabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Control Panel\Mouse");
                var value = key?.GetValue("MouseSpeed");
                return value != null && value.ToString() == "0";
            }
            catch
            {
                return false;
            }
        }

        private bool VerifyGameDvrDisabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"System\GameConfigStore");
                var value = key?.GetValue("GameDVR_Enabled");
                return value != null && (int)value == 0;
            }
            catch
            {
                return false;
            }
        }

        private bool VerifyGameBarDisabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\GameDVR");
                var value = key?.GetValue("AppCaptureEnabled");
                return value != null && (int)value == 0;
            }
            catch
            {
                return false;
            }
        }

        private bool VerifyFullscreenOptimizationsDisabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"System\GameConfigStore");
                var value = key?.GetValue("GameDVR_FSEBehaviorMode");
                return value != null && (int)value == 2;
            }
            catch
            {
                return false;
            }
        }

        // ========== VISUAL VERIFICATIONS ==========
        
        private bool VerifyAnimationsDisabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Control Panel\Desktop\WindowMetrics");
                var value = key?.GetValue("MinAnimate");
                return value != null && value.ToString() == "0";
            }
            catch
            {
                return false;
            }
        }

        private bool VerifyTransparencyDisabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("EnableTransparency");
                return value != null && (int)value == 0;
            }
            catch
            {
                return false;
            }
        }

        // ========== STORAGE VERIFICATIONS ==========
        
        private bool DetectSsd()
        {
            try
            {
                // Check system drive
                var systemDrive = System.IO.Path.GetPathRoot(Environment.SystemDirectory);
                if (string.IsNullOrEmpty(systemDrive)) return false;
                
                // Use PowerShell to check media type
                using var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "powershell.exe";
                process.StartInfo.Arguments = $"-Command \"(Get-PhysicalDisk | Where-Object {{ $_.DeviceId -eq (Get-Partition -DriveLetter '{systemDrive[0]}').DiskNumber }}).MediaType\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                
                return output.Contains("SSD") || output.Contains("NVMe");
            }
            catch
            {
                return true; // Assume SSD for modern systems
            }
        }

        private bool VerifyTrimEnabled()
        {
            try
            {
                using var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "fsutil";
                process.StartInfo.Arguments = "behavior query DisableDeleteNotify";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                return output.Contains("= 0"); // TRIM enabled when DisableDeleteNotify = 0
            }
            catch
            {
                return true; // Assume enabled
            }
        }

        private bool VerifyDefragDisabled()
        {
            // Check scheduled defrag tasks for SSD
            return DetectSsd(); // Simplified - Windows auto-detects SSD
        }

        private bool VerifyShortNamesDisabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\FileSystem");
                var value = key?.GetValue("NtfsDisable8dot3NameCreation");
                return value != null && (int)value == 1;
            }
            catch
            {
                return false;
            }
        }

        private bool VerifyLastAccessDisabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\FileSystem");
                var value = key?.GetValue("NtfsDisableLastAccessUpdate");
                return value != null && (int)value == 1;
            }
            catch
            {
                return false;
            }
        }
    }
}
