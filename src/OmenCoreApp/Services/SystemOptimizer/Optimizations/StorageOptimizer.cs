using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OmenCore.Services.SystemOptimizer.Optimizations
{
    /// <summary>
    /// Handles storage optimizations: SSD detection, TRIM, 8.3 names, last access timestamps.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class StorageOptimizer
    {
        private readonly LoggingService _logger;
        private readonly RegistryBackupService _backup;

        public StorageOptimizer(LoggingService logger, RegistryBackupService backup)
        {
            _logger = logger;
            _backup = backup;
        }

        public async Task<StorageOptimizationState> GetStateAsync()
        {
            return await Task.Run(() => new StorageOptimizationState
            {
                IsSsd = DetectSSD(),
                TrimEnabled = IsTRIMEnabled(),
                LastAccessDisabled = IsLastAccessDisabled(),
                ShortNamesDisabled = Is8Dot3NamesDisabled()
            });
        }

        public async Task<List<OptimizationResult>> ApplyAllAsync()
        {
            var results = new List<OptimizationResult>();
            
            if (DetectSSD())
            {
                results.Add(await EnableTRIMAsync());
            }
            
            results.Add(await DisableLastAccessAsync());
            results.Add(await Disable8Dot3NamesAsync());
            results.Add(await DisablePrefetchForSSDAsync());
            
            return results;
        }

        public async Task<List<OptimizationResult>> ApplyRecommendedAsync()
        {
            var results = new List<OptimizationResult>();
            
            // Recommended: Disable last access timestamps (safe, minor performance gain)
            results.Add(await DisableLastAccessAsync());
            
            if (DetectSSD())
            {
                results.Add(await EnableTRIMAsync());
            }
            
            return results;
        }

        public async Task<List<OptimizationResult>> RevertAllAsync()
        {
            var results = new List<OptimizationResult>();
            
            results.Add(await DisableTRIMAsync());
            results.Add(await EnableLastAccessAsync());
            results.Add(await Enable8Dot3NamesAsync());
            results.Add(await EnablePrefetchAsync());
            
            return results;
        }

        public async Task<OptimizationResult> ApplyAsync(string optimizationId)
        {
            return optimizationId switch
            {
                "storage_trim" => await EnableTRIMAsync(),
                "storage_last_access" => await DisableLastAccessAsync(),
                "storage_8dot3" => await Disable8Dot3NamesAsync(),
                "storage_prefetch" => await DisablePrefetchForSSDAsync(),
                _ => new OptimizationResult { Id = optimizationId, Success = false, ErrorMessage = "Unknown optimization" }
            };
        }

        public async Task<OptimizationResult> RevertAsync(string optimizationId)
        {
            return optimizationId switch
            {
                "storage_trim" => await DisableTRIMAsync(),
                "storage_last_access" => await EnableLastAccessAsync(),
                "storage_8dot3" => await Enable8Dot3NamesAsync(),
                "storage_prefetch" => await EnablePrefetchAsync(),
                _ => new OptimizationResult { Id = optimizationId, Success = false, ErrorMessage = "Unknown optimization" }
            };
        }

        // ========== SSD DETECTION ==========

        public bool DetectSSD()
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\microsoft\windows\storage");
                scope.Connect();
                
                var query = new ObjectQuery("SELECT * FROM MSFT_PhysicalDisk");
                using var searcher = new ManagementObjectSearcher(scope, query);
                
                foreach (ManagementObject disk in searcher.Get())
                {
                    var mediaType = disk["MediaType"]?.ToString();
                    // MediaType: 3 = HDD, 4 = SSD, 5 = SCM
                    if (mediaType == "4" || mediaType == "5")
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Could not detect SSD via WMI: {ex.Message}");
                
                // Fallback: Check if C: drive has no seek penalty
                try
                {
                    var value = _backup.GetRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Services\storahci\Parameters\Device",
                        "TrimEnabled");
                    
                    if (value != null)
                    {
                        return true; // TRIM being present suggests SSD
                    }
                }
                catch { }
            }
            
            return false;
        }

        // ========== TRIM ==========

        private async Task<OptimizationResult> EnableTRIMAsync()
        {
            var result = new OptimizationResult
            {
                Id = "storage_trim",
                Name = "TRIM for SSD",
                Category = "Storage"
            };

            try
            {
                await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "fsutil",
                        Arguments = "behavior set DisableDeleteNotify 0",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    };
                    
                    using var process = Process.Start(psi);
                    process?.WaitForExit();
                    
                    result.Success = process?.ExitCode == 0;
                    if (!result.Success)
                    {
                        result.ErrorMessage = "fsutil command failed";
                    }
                });
                
                if (result.Success)
                {
                    _logger.Info("TRIM enabled for SSD");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> DisableTRIMAsync()
        {
            var result = new OptimizationResult
            {
                Id = "storage_trim",
                Name = "TRIM for SSD",
                Category = "Storage"
            };

            try
            {
                await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "fsutil",
                        Arguments = "behavior set DisableDeleteNotify 1",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    };
                    
                    using var process = Process.Start(psi);
                    process?.WaitForExit();
                    
                    result.Success = process?.ExitCode == 0;
                });
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ========== LAST ACCESS TIMESTAMPS ==========

        private async Task<OptimizationResult> DisableLastAccessAsync()
        {
            var result = new OptimizationResult
            {
                Id = "storage_last_access",
                Name = "Last Access Timestamps",
                Category = "Storage"
            };

            try
            {
                await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "fsutil",
                        Arguments = "behavior set disablelastaccess 1",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    };
                    
                    using var process = Process.Start(psi);
                    process?.WaitForExit();
                    
                    result.Success = process?.ExitCode == 0;
                    if (!result.Success)
                    {
                        result.ErrorMessage = "fsutil command failed";
                    }
                });
                
                if (result.Success)
                {
                    _logger.Info("Last access timestamps disabled");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> EnableLastAccessAsync()
        {
            var result = new OptimizationResult
            {
                Id = "storage_last_access",
                Name = "Last Access Timestamps",
                Category = "Storage"
            };

            try
            {
                await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "fsutil",
                        Arguments = "behavior set disablelastaccess 0",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    };
                    
                    using var process = Process.Start(psi);
                    process?.WaitForExit();
                    
                    result.Success = process?.ExitCode == 0;
                });
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ========== 8.3 FILE NAMES ==========

        private async Task<OptimizationResult> Disable8Dot3NamesAsync()
        {
            var result = new OptimizationResult
            {
                Id = "storage_8dot3",
                Name = "8.3 Short File Names",
                Category = "Storage"
            };

            try
            {
                await Task.Run(() =>
                {
                    // NtfsDisable8dot3NameCreation: 1 = disable
                    _backup.SetRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem",
                        "NtfsDisable8dot3NameCreation",
                        1,
                        RegistryValueKind.DWord);
                    
                    result.Success = true;
                });
                
                _logger.Info("8.3 short file name creation disabled");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> Enable8Dot3NamesAsync()
        {
            var result = new OptimizationResult
            {
                Id = "storage_8dot3",
                Name = "8.3 Short File Names",
                Category = "Storage"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.RestoreRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem",
                        "NtfsDisable8dot3NameCreation");
                    result.Success = true;
                });
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ========== PREFETCH/SUPERFETCH FOR SSD ==========

        private async Task<OptimizationResult> DisablePrefetchForSSDAsync()
        {
            var result = new OptimizationResult
            {
                Id = "storage_prefetch",
                Name = "Prefetch (SSD Optimization)",
                Category = "Storage"
            };

            try
            {
                if (!DetectSSD())
                {
                    result.Success = false;
                    result.ErrorMessage = "No SSD detected - keeping Prefetch enabled";
                    return result;
                }
                
                await Task.Run(() =>
                {
                    // Disable Prefetcher for SSD (not beneficial)
                    _backup.SetRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters",
                        "EnablePrefetcher",
                        0,
                        RegistryValueKind.DWord);
                    
                    _backup.SetRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters",
                        "EnableSuperfetch",
                        0,
                        RegistryValueKind.DWord);
                    
                    result.Success = true;
                });
                
                _logger.Info("Prefetch/Superfetch disabled (SSD detected)");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> EnablePrefetchAsync()
        {
            var result = new OptimizationResult
            {
                Id = "storage_prefetch",
                Name = "Prefetch (SSD Optimization)",
                Category = "Storage"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.RestoreRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters",
                        "EnablePrefetcher");
                    
                    _backup.RestoreRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters",
                        "EnableSuperfetch");
                    
                    result.Success = true;
                });
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ========== STATE CHECKS ==========

        private bool IsTRIMEnabled()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "fsutil",
                    Arguments = "behavior query DisableDeleteNotify",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                
                using var process = Process.Start(psi);
                var output = process?.StandardOutput.ReadToEnd() ?? "";
                process?.WaitForExit();
                
                // "DisableDeleteNotify = 0" means TRIM is enabled
                return output.Contains("= 0") || output.Contains("=0");
            }
            catch
            {
                return false;
            }
        }

        private bool IsLastAccessDisabled()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "fsutil",
                    Arguments = "behavior query disablelastaccess",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                
                using var process = Process.Start(psi);
                var output = process?.StandardOutput.ReadToEnd() ?? "";
                process?.WaitForExit();
                
                // Values: 0=enabled, 1=disabled, 2=system managed disabled, 3=system managed enabled
                return output.Contains("= 1") || output.Contains("= 2") || 
                       output.Contains("=1") || output.Contains("=2");
            }
            catch
            {
                return false;
            }
        }

        private bool Is8Dot3NamesDisabled()
        {
            var value = _backup.GetRegistryValue(
                @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem",
                "NtfsDisable8dot3NameCreation");
            return value != null && (int)value == 1;
        }
    }
}
