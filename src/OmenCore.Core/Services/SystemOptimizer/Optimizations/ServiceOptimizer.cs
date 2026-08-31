using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OmenCore.Services.SystemOptimizer.Optimizations
{
    /// <summary>
    /// Handles Windows service optimizations: telemetry, SysMain, Search indexing, DiagTrack.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class ServiceOptimizer : IDisposable
    {
        private readonly LoggingService _logger;
        private readonly RegistryBackupService _backup;

        public ServiceOptimizer(LoggingService logger, RegistryBackupService backup)
        {
            _logger = logger;
            _backup = backup;
        }

        public async Task<ServiceOptimizationState> GetStateAsync()
        {
            return await Task.Run(() => new ServiceOptimizationState
            {
                TelemetryDisabled = IsTelemetryDisabled(),
                SysMainDisabled = IsServiceStopped("SysMain"),
                SearchIndexingDisabled = IsServiceStopped("WSearch"),
                DiagTrackDisabled = IsServiceStopped("DiagTrack")
            });
        }

        public async Task<List<OptimizationResult>> ApplyAllAsync()
        {
            var results = new List<OptimizationResult>
            {
                await DisableTelemetryAsync(),
                await DisableSysMainAsync(),
                await DisableSearchIndexingAsync(),
                await DisableDiagTrackAsync()
            };

            return results;
        }

        public async Task<List<OptimizationResult>> ApplyRecommendedAsync()
        {
            var results = new List<OptimizationResult>
            {

                // Recommended: Telemetry and DiagTrack only (SysMain/Search may be useful for some)
                await DisableTelemetryAsync(),
                await DisableDiagTrackAsync()
            };

            return results;
        }

        public async Task<List<OptimizationResult>> RevertAllAsync()
        {
            var results = new List<OptimizationResult>
            {
                await EnableTelemetryAsync(),
                await EnableSysMainAsync(),
                await EnableSearchIndexingAsync(),
                await EnableDiagTrackAsync()
            };

            return results;
        }

        public async Task<OptimizationResult> ApplyAsync(string optimizationId)
        {
            return optimizationId switch
            {
                "service_telemetry" => await DisableTelemetryAsync(),
                "service_sysmain" => await DisableSysMainAsync(),
                "service_search" => await DisableSearchIndexingAsync(),
                "service_diagtrack" => await DisableDiagTrackAsync(),
                _ => new OptimizationResult { Id = optimizationId, Success = false, ErrorMessage = "Unknown optimization" }
            };
        }

        public async Task<OptimizationResult> RevertAsync(string optimizationId)
        {
            return optimizationId switch
            {
                "service_telemetry" => await EnableTelemetryAsync(),
                "service_sysmain" => await EnableSysMainAsync(),
                "service_search" => await EnableSearchIndexingAsync(),
                "service_diagtrack" => await EnableDiagTrackAsync(),
                _ => new OptimizationResult { Id = optimizationId, Success = false, ErrorMessage = "Unknown optimization" }
            };
        }

        // ========== TELEMETRY ==========

        private async Task<OptimizationResult> DisableTelemetryAsync()
        {
            var result = new OptimizationResult
            {
                Id = "service_telemetry",
                Name = "Windows Telemetry",
                Category = "Services"
            };

            try
            {
                await Task.Run(() =>
                {
                    // Disable telemetry via policy
                    _backup.SetRegistryValue(
                        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                        "AllowTelemetry",
                        0,
                        RegistryValueKind.DWord);
                    
                    // Also set in main registry
                    _backup.SetRegistryValue(
                        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection",
                        "AllowTelemetry",
                        0,
                        RegistryValueKind.DWord);

                    result.Success = true;
                });
                
                _logger.Info("Telemetry disabled");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> EnableTelemetryAsync()
        {
            var result = new OptimizationResult
            {
                Id = "service_telemetry",
                Name = "Windows Telemetry",
                Category = "Services"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.RestoreRegistryValue(
                        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                        "AllowTelemetry");
                    _backup.RestoreRegistryValue(
                        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection",
                        "AllowTelemetry");
                    result.Success = true;
                });
                
                _logger.Info("Telemetry restored to default");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ========== SYSMAIN (SUPERFETCH) ==========

        private async Task<OptimizationResult> DisableSysMainAsync()
        {
            var result = new OptimizationResult
            {
                Id = "service_sysmain",
                Name = "SysMain (Superfetch)",
                Category = "Services",
                RequiresReboot = false
            };

            try
            {
                await Task.Run(() =>
                {
                    result.Success = StopAndDisableService("SysMain");
                });
                
                _logger.Info($"SysMain disabled: {result.Success}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> EnableSysMainAsync()
        {
            var result = new OptimizationResult
            {
                Id = "service_sysmain",
                Name = "SysMain (Superfetch)",
                Category = "Services"
            };

            try
            {
                await Task.Run(() =>
                {
                    result.Success = EnableAndStartService("SysMain");
                });
                
                _logger.Info($"SysMain enabled: {result.Success}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ========== WINDOWS SEARCH ==========

        private async Task<OptimizationResult> DisableSearchIndexingAsync()
        {
            var result = new OptimizationResult
            {
                Id = "service_search",
                Name = "Windows Search Indexing",
                Category = "Services"
            };

            try
            {
                await Task.Run(() =>
                {
                    result.Success = StopAndDisableService("WSearch");
                });
                
                _logger.Info($"Search indexing disabled: {result.Success}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> EnableSearchIndexingAsync()
        {
            var result = new OptimizationResult
            {
                Id = "service_search",
                Name = "Windows Search Indexing",
                Category = "Services"
            };

            try
            {
                await Task.Run(() =>
                {
                    result.Success = EnableAndStartService("WSearch");
                });
                
                _logger.Info($"Search indexing enabled: {result.Success}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ========== DIAGTRACK (CONNECTED USER EXPERIENCES) ==========

        private async Task<OptimizationResult> DisableDiagTrackAsync()
        {
            var result = new OptimizationResult
            {
                Id = "service_diagtrack",
                Name = "Connected User Experiences (DiagTrack)",
                Category = "Services"
            };

            try
            {
                await Task.Run(() =>
                {
                    result.Success = StopAndDisableService("DiagTrack");
                });
                
                _logger.Info($"DiagTrack disabled: {result.Success}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> EnableDiagTrackAsync()
        {
            var result = new OptimizationResult
            {
                Id = "service_diagtrack",
                Name = "Connected User Experiences (DiagTrack)",
                Category = "Services"
            };

            try
            {
                await Task.Run(() =>
                {
                    result.Success = EnableAndStartService("DiagTrack");
                });
                
                _logger.Info($"DiagTrack enabled: {result.Success}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ========== HELPERS ==========

        private bool StopAndDisableService(string serviceName)
        {
            try
            {
                // Use sc.exe for more reliable service control
                using var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "sc.exe";
                process.StartInfo.Arguments = $"stop {serviceName}";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit(10000);

                // Disable the service
                process.StartInfo.Arguments = $"config {serviceName} start= disabled";
                process.Start();
                process.WaitForExit(5000);

                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to stop/disable {serviceName}: {ex.Message}");
                return false;
            }
        }

        private bool EnableAndStartService(string serviceName)
        {
            try
            {
                using var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "sc.exe";
                process.StartInfo.Arguments = $"config {serviceName} start= auto";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit(5000);

                process.StartInfo.Arguments = $"start {serviceName}";
                process.Start();
                process.WaitForExit(10000);

                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to enable/start {serviceName}: {ex.Message}");
                return false;
            }
        }

        private bool IsServiceStopped(string serviceName)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                return sc.Status == ServiceControllerStatus.Stopped;
            }
            catch
            {
                return false;
            }
        }

        private bool IsTelemetryDisabled()
        {
            var value = _backup.GetRegistryValue(
                @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                "AllowTelemetry");
            return value != null && (int)value == 0;
        }

        public void Dispose()
        {
            // Nothing to dispose
        }
    }
}
