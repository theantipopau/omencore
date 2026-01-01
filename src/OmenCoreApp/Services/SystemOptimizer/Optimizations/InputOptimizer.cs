using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OmenCore.Services.SystemOptimizer.Optimizations
{
    /// <summary>
    /// Handles input optimizations: Mouse acceleration, Game DVR, Game Bar, fullscreen optimizations.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class InputOptimizer
    {
        private readonly LoggingService _logger;
        private readonly RegistryBackupService _backup;

        public InputOptimizer(LoggingService logger, RegistryBackupService backup)
        {
            _logger = logger;
            _backup = backup;
        }

        public async Task<InputOptimizationState> GetStateAsync()
        {
            return await Task.Run(() => new InputOptimizationState
            {
                MouseAccelerationDisabled = IsMouseAccelerationDisabled(),
                GameDvrDisabled = IsGameDVRDisabled(),
                GameBarDisabled = IsGameBarDisabled(),
                FullscreenOptimizationsDisabled = !IsFullscreenOptimizationsEnabled()
            });
        }

        public async Task<List<OptimizationResult>> ApplyAllAsync()
        {
            var results = new List<OptimizationResult>
            {
                await DisableMouseAccelerationAsync(),
                await DisableGameDVRAsync(),
                await DisableGameBarAsync(),
                await EnableFullscreenOptimizationsAsync()
            };

            return results;
        }

        public async Task<List<OptimizationResult>> ApplyRecommendedAsync()
        {
            var results = new List<OptimizationResult>
            {

                // Recommended: Disable Game DVR (high impact), leave mouse alone
                await DisableGameDVRAsync(),
                await EnableFullscreenOptimizationsAsync()
            };

            return results;
        }

        public async Task<List<OptimizationResult>> RevertAllAsync()
        {
            var results = new List<OptimizationResult>
            {
                await EnableMouseAccelerationAsync(),
                await EnableGameDVRAsync(),
                await EnableGameBarAsync(),
                await RevertFullscreenOptimizationsAsync()
            };

            return results;
        }

        public async Task<OptimizationResult> ApplyAsync(string optimizationId)
        {
            return optimizationId switch
            {
                "input_mouse_accel" => await DisableMouseAccelerationAsync(),
                "input_game_dvr" => await DisableGameDVRAsync(),
                "input_game_bar" => await DisableGameBarAsync(),
                "input_fullscreen_opt" => await EnableFullscreenOptimizationsAsync(),
                _ => new OptimizationResult { Id = optimizationId, Success = false, ErrorMessage = "Unknown optimization" }
            };
        }

        public async Task<OptimizationResult> RevertAsync(string optimizationId)
        {
            return optimizationId switch
            {
                "input_mouse_accel" => await EnableMouseAccelerationAsync(),
                "input_game_dvr" => await EnableGameDVRAsync(),
                "input_game_bar" => await EnableGameBarAsync(),
                "input_fullscreen_opt" => await RevertFullscreenOptimizationsAsync(),
                _ => new OptimizationResult { Id = optimizationId, Success = false, ErrorMessage = "Unknown optimization" }
            };
        }

        // ========== MOUSE ACCELERATION ==========

        private async Task<OptimizationResult> DisableMouseAccelerationAsync()
        {
            var result = new OptimizationResult
            {
                Id = "input_mouse_accel",
                Name = "Mouse Acceleration (Enhance Pointer Precision)",
                Category = "Input"
            };

            try
            {
                await Task.Run(() =>
                {
                    // MouseSpeed = 0 disables enhanced pointer precision
                    _backup.SetRegistryValue(
                        @"HKCU\Control Panel\Mouse",
                        "MouseSpeed",
                        "0",
                        RegistryValueKind.String);
                    
                    // Set flat acceleration curve
                    _backup.SetRegistryValue(
                        @"HKCU\Control Panel\Mouse",
                        "MouseThreshold1",
                        "0",
                        RegistryValueKind.String);
                    
                    _backup.SetRegistryValue(
                        @"HKCU\Control Panel\Mouse",
                        "MouseThreshold2",
                        "0",
                        RegistryValueKind.String);
                    
                    result.Success = true;
                });
                
                _logger.Info("Mouse acceleration disabled (Enhance Pointer Precision off)");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> EnableMouseAccelerationAsync()
        {
            var result = new OptimizationResult
            {
                Id = "input_mouse_accel",
                Name = "Mouse Acceleration (Enhance Pointer Precision)",
                Category = "Input"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.RestoreRegistryValue(@"HKCU\Control Panel\Mouse", "MouseSpeed");
                    _backup.RestoreRegistryValue(@"HKCU\Control Panel\Mouse", "MouseThreshold1");
                    _backup.RestoreRegistryValue(@"HKCU\Control Panel\Mouse", "MouseThreshold2");
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

        // ========== GAME DVR ==========

        private async Task<OptimizationResult> DisableGameDVRAsync()
        {
            var result = new OptimizationResult
            {
                Id = "input_game_dvr",
                Name = "Game DVR (Background Recording)",
                Category = "Input"
            };

            try
            {
                await Task.Run(() =>
                {
                    // Disable Game DVR
                    _backup.SetRegistryValue(
                        @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR",
                        "AppCaptureEnabled",
                        0,
                        RegistryValueKind.DWord);
                    
                    // Disable background recording
                    _backup.SetRegistryValue(
                        @"HKCU\System\GameConfigStore",
                        "GameDVR_Enabled",
                        0,
                        RegistryValueKind.DWord);
                    
                    // Policy-based disable
                    _backup.SetRegistryValue(
                        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR",
                        "AllowGameDVR",
                        0,
                        RegistryValueKind.DWord);
                    
                    result.Success = true;
                });
                
                _logger.Info("Game DVR disabled");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> EnableGameDVRAsync()
        {
            var result = new OptimizationResult
            {
                Id = "input_game_dvr",
                Name = "Game DVR (Background Recording)",
                Category = "Input"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.RestoreRegistryValue(
                        @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR",
                        "AppCaptureEnabled");
                    
                    _backup.RestoreRegistryValue(
                        @"HKCU\System\GameConfigStore",
                        "GameDVR_Enabled");
                    
                    _backup.RestoreRegistryValue(
                        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR",
                        "AllowGameDVR");
                    
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

        // ========== GAME BAR ==========

        private async Task<OptimizationResult> DisableGameBarAsync()
        {
            var result = new OptimizationResult
            {
                Id = "input_game_bar",
                Name = "Xbox Game Bar",
                Category = "Input"
            };

            try
            {
                await Task.Run(() =>
                {
                    // Disable Game Bar
                    _backup.SetRegistryValue(
                        @"HKCU\SOFTWARE\Microsoft\GameBar",
                        "UseNexusForGameBarEnabled",
                        0,
                        RegistryValueKind.DWord);
                    
                    // Disable Game Bar tips
                    _backup.SetRegistryValue(
                        @"HKCU\SOFTWARE\Microsoft\GameBar",
                        "ShowStartupPanel",
                        0,
                        RegistryValueKind.DWord);
                    
                    result.Success = true;
                });
                
                _logger.Info("Xbox Game Bar disabled");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> EnableGameBarAsync()
        {
            var result = new OptimizationResult
            {
                Id = "input_game_bar",
                Name = "Xbox Game Bar",
                Category = "Input"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.RestoreRegistryValue(
                        @"HKCU\SOFTWARE\Microsoft\GameBar",
                        "UseNexusForGameBarEnabled");
                    
                    _backup.RestoreRegistryValue(
                        @"HKCU\SOFTWARE\Microsoft\GameBar",
                        "ShowStartupPanel");
                    
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

        // ========== FULLSCREEN OPTIMIZATIONS ==========

        private async Task<OptimizationResult> EnableFullscreenOptimizationsAsync()
        {
            var result = new OptimizationResult
            {
                Id = "input_fullscreen_opt",
                Name = "Fullscreen Optimizations (System-wide)",
                Category = "Input"
            };

            try
            {
                await Task.Run(() =>
                {
                    // Enable FSO globally (value 2 enables, 3 disables)
                    _backup.SetRegistryValue(
                        @"HKCU\System\GameConfigStore",
                        "GameDVR_FSEBehaviorMode",
                        2,
                        RegistryValueKind.DWord);
                    
                    _backup.SetRegistryValue(
                        @"HKCU\System\GameConfigStore",
                        "GameDVR_HonorUserFSEBehaviorMode",
                        1,
                        RegistryValueKind.DWord);
                    
                    _backup.SetRegistryValue(
                        @"HKCU\System\GameConfigStore",
                        "GameDVR_FSEBehavior",
                        2,
                        RegistryValueKind.DWord);
                    
                    result.Success = true;
                });
                
                _logger.Info("Fullscreen optimizations configured system-wide");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> RevertFullscreenOptimizationsAsync()
        {
            var result = new OptimizationResult
            {
                Id = "input_fullscreen_opt",
                Name = "Fullscreen Optimizations (System-wide)",
                Category = "Input"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.RestoreRegistryValue(
                        @"HKCU\System\GameConfigStore",
                        "GameDVR_FSEBehaviorMode");
                    
                    _backup.RestoreRegistryValue(
                        @"HKCU\System\GameConfigStore",
                        "GameDVR_HonorUserFSEBehaviorMode");
                    
                    _backup.RestoreRegistryValue(
                        @"HKCU\System\GameConfigStore",
                        "GameDVR_FSEBehavior");
                    
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

        private bool IsMouseAccelerationDisabled()
        {
            var value = _backup.GetRegistryValue(@"HKCU\Control Panel\Mouse", "MouseSpeed");
            return value != null && value.ToString() == "0";
        }

        private bool IsGameDVRDisabled()
        {
            var value = _backup.GetRegistryValue(
                @"HKCU\System\GameConfigStore",
                "GameDVR_Enabled");
            return value != null && (int)value == 0;
        }

        private bool IsGameBarDisabled()
        {
            var value = _backup.GetRegistryValue(
                @"HKCU\SOFTWARE\Microsoft\GameBar",
                "UseNexusForGameBarEnabled");
            return value != null && (int)value == 0;
        }

        private bool IsFullscreenOptimizationsEnabled()
        {
            var value = _backup.GetRegistryValue(
                @"HKCU\System\GameConfigStore",
                "GameDVR_FSEBehaviorMode");
            return value != null && (int)value == 2;
        }
    }
}
