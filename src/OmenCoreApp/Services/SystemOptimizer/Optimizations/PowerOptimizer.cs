using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OmenCore.Services.SystemOptimizer.Optimizations
{
    /// <summary>
    /// Handles power-related optimizations: Ultimate Performance plan, GPU scheduling, Game Mode, priority.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class PowerOptimizer : IDisposable
    {
        private readonly LoggingService _logger;
        private readonly RegistryBackupService _backup;

        // Power plan GUIDs
        private const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

        public PowerOptimizer(LoggingService logger, RegistryBackupService backup)
        {
            _logger = logger;
            _backup = backup;
        }

        public async Task<PowerOptimizationState> GetStateAsync()
        {
            return await Task.Run(() => new PowerOptimizationState
            {
                UltimatePerformancePlan = IsUltimateOrHighPerformanceActive(),
                HardwareGpuScheduling = IsHardwareGpuSchedulingEnabled(),
                GameModeEnabled = IsGameModeEnabled(),
                ForegroundPriority = IsForegroundPriorityOptimized()
            });
        }

        public async Task<List<OptimizationResult>> ApplyAllAsync()
        {
            var results = new List<OptimizationResult>
            {
                await ApplyUltimatePerformancePlanAsync(),
                await ApplyHardwareGpuSchedulingAsync(),
                await ApplyGameModeAsync(),
                await ApplyForegroundPriorityAsync()
            };

            return results;
        }

        public async Task<List<OptimizationResult>> ApplyRecommendedAsync()
        {
            var results = new List<OptimizationResult>
            {

                // Recommended: High Performance (not Ultimate), GPU scheduling, Game Mode, priority
                await ApplyHighPerformancePlanAsync(),
                await ApplyHardwareGpuSchedulingAsync(),
                await ApplyGameModeAsync(),
                await ApplyForegroundPriorityAsync()
            };

            return results;
        }

        public async Task<List<OptimizationResult>> RevertAllAsync()
        {
            var results = new List<OptimizationResult>
            {
                await RevertToBalancedPlanAsync(),
                // GPU scheduling and Game Mode don't need explicit revert
                await RevertForegroundPriorityAsync()
            };

            return results;
        }

        public async Task<OptimizationResult> ApplyAsync(string optimizationId)
        {
            return optimizationId switch
            {
                "power_ultimate" => await ApplyUltimatePerformancePlanAsync(),
                "power_high" => await ApplyHighPerformancePlanAsync(),
                "power_gpu_scheduling" => await ApplyHardwareGpuSchedulingAsync(),
                "power_game_mode" => await ApplyGameModeAsync(),
                "power_foreground_priority" => await ApplyForegroundPriorityAsync(),
                _ => new OptimizationResult { Id = optimizationId, Success = false, ErrorMessage = "Unknown optimization" }
            };
        }

        public async Task<OptimizationResult> RevertAsync(string optimizationId)
        {
            return optimizationId switch
            {
                "power_ultimate" or "power_high" => await RevertToBalancedPlanAsync(),
                "power_foreground_priority" => await RevertForegroundPriorityAsync(),
                _ => new OptimizationResult { Id = optimizationId, Success = true } // No-op for others
            };
        }

        // ========== IMPLEMENTATIONS ==========

        private async Task<OptimizationResult> ApplyUltimatePerformancePlanAsync()
        {
            var result = new OptimizationResult
            {
                Id = "power_ultimate",
                Name = "Ultimate Performance Power Plan",
                Category = "Power"
            };

            try
            {
                await Task.Run(() =>
                {
                    // First, try to unhide Ultimate Performance (it's hidden by default on laptops)
                    RunPowerCfg($"/duplicatescheme {UltimatePerformanceGuid}");
                    
                    // Activate it
                    var exitCode = RunPowerCfg($"/setactive {UltimatePerformanceGuid}");
                    
                    if (exitCode != 0)
                    {
                        // Fall back to High Performance
                        exitCode = RunPowerCfg($"/setactive {HighPerformanceGuid}");
                    }
                    
                    result.Success = exitCode == 0;
                });
                
                _logger.Info($"Power plan set to Ultimate/High Performance: {result.Success}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.Error($"Failed to set power plan: {ex.Message}");
            }

            return result;
        }

        private async Task<OptimizationResult> ApplyHighPerformancePlanAsync()
        {
            var result = new OptimizationResult
            {
                Id = "power_high",
                Name = "High Performance Power Plan",
                Category = "Power"
            };

            try
            {
                await Task.Run(() =>
                {
                    var exitCode = RunPowerCfg($"/setactive {HighPerformanceGuid}");
                    result.Success = exitCode == 0;
                });
                
                _logger.Info($"Power plan set to High Performance: {result.Success}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> RevertToBalancedPlanAsync()
        {
            var result = new OptimizationResult
            {
                Id = "power_balanced",
                Name = "Balanced Power Plan",
                Category = "Power"
            };

            try
            {
                await Task.Run(() =>
                {
                    var exitCode = RunPowerCfg($"/setactive {BalancedGuid}");
                    result.Success = exitCode == 0;
                });
                
                _logger.Info($"Power plan reverted to Balanced: {result.Success}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> ApplyHardwareGpuSchedulingAsync()
        {
            var result = new OptimizationResult
            {
                Id = "power_gpu_scheduling",
                Name = "Hardware-accelerated GPU Scheduling",
                Category = "Power",
                RequiresReboot = true
            };

            try
            {
                await Task.Run(() =>
                {
                    result.Success = _backup.SetRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                        "HwSchMode",
                        2,
                        RegistryValueKind.DWord);
                });
                
                _logger.Info($"Hardware GPU scheduling enabled: {result.Success}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> ApplyGameModeAsync()
        {
            var result = new OptimizationResult
            {
                Id = "power_game_mode",
                Name = "Windows Game Mode",
                Category = "Power"
            };

            try
            {
                await Task.Run(() =>
                {
                    result.Success = _backup.SetRegistryValue(
                        @"HKCU\Software\Microsoft\GameBar",
                        "AutoGameModeEnabled",
                        1,
                        RegistryValueKind.DWord);
                });
                
                _logger.Info($"Game Mode enabled: {result.Success}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> ApplyForegroundPriorityAsync()
        {
            var result = new OptimizationResult
            {
                Id = "power_foreground_priority",
                Name = "Foreground Application Priority",
                Category = "Power"
            };

            try
            {
                await Task.Run(() =>
                {
                    // Win32PrioritySeparation = 38 (0x26) = Short, Fixed, High foreground boost
                    // This gives games maximum CPU priority
                    result.Success = _backup.SetRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl",
                        "Win32PrioritySeparation",
                        38,
                        RegistryValueKind.DWord);
                });
                
                _logger.Info($"Foreground priority optimized: {result.Success}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> RevertForegroundPriorityAsync()
        {
            var result = new OptimizationResult
            {
                Id = "power_foreground_priority",
                Name = "Foreground Application Priority",
                Category = "Power"
            };

            try
            {
                await Task.Run(() =>
                {
                    // Default Windows value = 2 (or 0x2)
                    result.Success = _backup.RestoreRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl",
                        "Win32PrioritySeparation");
                });
                
                _logger.Info($"Foreground priority reverted: {result.Success}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ========== STATE CHECKS ==========

        private bool IsUltimateOrHighPerformanceActive()
        {
            try
            {
                using var process = new Process();
                process.StartInfo.FileName = "powercfg";
                process.StartInfo.Arguments = "/getactivescheme";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                return output.Contains(UltimatePerformanceGuid) || 
                       output.Contains(HighPerformanceGuid);
            }
            catch
            {
                return false;
            }
        }

        private bool IsHardwareGpuSchedulingEnabled()
        {
            var value = _backup.GetRegistryValue(
                @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                "HwSchMode");
            return value != null && (int)value == 2;
        }

        private bool IsGameModeEnabled()
        {
            var value = _backup.GetRegistryValue(
                @"HKCU\Software\Microsoft\GameBar",
                "AutoGameModeEnabled");
            return value == null || (int)value == 1; // Default is enabled
        }

        private bool IsForegroundPriorityOptimized()
        {
            var value = _backup.GetRegistryValue(
                @"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl",
                "Win32PrioritySeparation");
            return value != null && ((int)value == 38 || (int)value == 26);
        }

        private int RunPowerCfg(string arguments)
        {
            using var process = new Process();
            process.StartInfo.FileName = "powercfg";
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode;
        }

        public void Dispose()
        {
            // Nothing to dispose
        }
    }
}
