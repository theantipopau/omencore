using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OmenCore.Services.SystemOptimizer.Optimizations
{
    /// <summary>
    /// Handles visual effects optimizations: Animations, transparency, visual presets.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class VisualEffectsOptimizer
    {
        private readonly LoggingService _logger;
        private readonly RegistryBackupService _backup;

        public VisualEffectsOptimizer(LoggingService logger, RegistryBackupService backup)
        {
            _logger = logger;
            _backup = backup;
        }

        public async Task<VisualOptimizationState> GetStateAsync()
        {
            return await Task.Run(() => new VisualOptimizationState
            {
                TransparencyDisabled = IsTransparencyDisabled(),
                AnimationsDisabled = IsAnimationsDisabled(),
                Mode = IsVisualEffectsOptimized() ? "Minimal" : "Default"
            });
        }

        public async Task<List<OptimizationResult>> ApplyAllAsync()
        {
            var results = new List<OptimizationResult>();
            
            results.Add(await DisableTransparencyAsync());
            results.Add(await DisableAnimationsAsync());
            results.Add(await DisableShadowsAsync());
            results.Add(await ApplyBestPerformanceAsync());
            
            return results;
        }

        public async Task<List<OptimizationResult>> ApplyRecommendedAsync()
        {
            var results = new List<OptimizationResult>();
            
            // Recommended: Disable animations only (biggest performance impact)
            results.Add(await DisableAnimationsAsync());
            
            return results;
        }

        public async Task<List<OptimizationResult>> RevertAllAsync()
        {
            var results = new List<OptimizationResult>();
            
            results.Add(await EnableTransparencyAsync());
            results.Add(await EnableAnimationsAsync());
            results.Add(await EnableShadowsAsync());
            results.Add(await RevertVisualEffectsAsync());
            
            return results;
        }

        public async Task<OptimizationResult> ApplyAsync(string optimizationId)
        {
            return optimizationId switch
            {
                "visual_transparency" => await DisableTransparencyAsync(),
                "visual_animations" => await DisableAnimationsAsync(),
                "visual_shadows" => await DisableShadowsAsync(),
                "visual_performance" => await ApplyBestPerformanceAsync(),
                _ => new OptimizationResult { Id = optimizationId, Success = false, ErrorMessage = "Unknown optimization" }
            };
        }

        public async Task<OptimizationResult> RevertAsync(string optimizationId)
        {
            return optimizationId switch
            {
                "visual_transparency" => await EnableTransparencyAsync(),
                "visual_animations" => await EnableAnimationsAsync(),
                "visual_shadows" => await EnableShadowsAsync(),
                "visual_performance" => await RevertVisualEffectsAsync(),
                _ => new OptimizationResult { Id = optimizationId, Success = false, ErrorMessage = "Unknown optimization" }
            };
        }

        // ========== TRANSPARENCY ==========

        private async Task<OptimizationResult> DisableTransparencyAsync()
        {
            var result = new OptimizationResult
            {
                Id = "visual_transparency",
                Name = "Transparency Effects",
                Category = "Visual"
            };

            try
            {
                await Task.Run(() =>
                {
                    // Disable transparency
                    _backup.SetRegistryValue(
                        @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                        "EnableTransparency",
                        0,
                        RegistryValueKind.DWord);
                    
                    result.Success = true;
                });
                
                _logger.Info("Transparency effects disabled");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> EnableTransparencyAsync()
        {
            var result = new OptimizationResult
            {
                Id = "visual_transparency",
                Name = "Transparency Effects",
                Category = "Visual"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.RestoreRegistryValue(
                        @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                        "EnableTransparency");
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

        // ========== ANIMATIONS ==========

        private async Task<OptimizationResult> DisableAnimationsAsync()
        {
            var result = new OptimizationResult
            {
                Id = "visual_animations",
                Name = "Window Animations",
                Category = "Visual"
            };

            try
            {
                await Task.Run(() =>
                {
                    // Disable window animations
                    _backup.SetRegistryValue(
                        @"HKCU\Control Panel\Desktop\WindowMetrics",
                        "MinAnimate",
                        "0",
                        RegistryValueKind.String);
                    
                    // Disable menu animations
                    _backup.SetRegistryValue(
                        @"HKCU\Control Panel\Desktop",
                        "MenuShowDelay",
                        "0",
                        RegistryValueKind.String);
                    
                    // Disable smooth-scroll (listboxes)
                    _backup.SetRegistryValue(
                        @"HKCU\Control Panel\Desktop",
                        "SmoothScroll",
                        0,
                        RegistryValueKind.DWord);
                    
                    result.Success = true;
                });
                
                _logger.Info("Window animations disabled");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> EnableAnimationsAsync()
        {
            var result = new OptimizationResult
            {
                Id = "visual_animations",
                Name = "Window Animations",
                Category = "Visual"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.RestoreRegistryValue(
                        @"HKCU\Control Panel\Desktop\WindowMetrics",
                        "MinAnimate");
                    
                    _backup.RestoreRegistryValue(
                        @"HKCU\Control Panel\Desktop",
                        "MenuShowDelay");
                    
                    _backup.RestoreRegistryValue(
                        @"HKCU\Control Panel\Desktop",
                        "SmoothScroll");
                    
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

        // ========== SHADOWS ==========

        private async Task<OptimizationResult> DisableShadowsAsync()
        {
            var result = new OptimizationResult
            {
                Id = "visual_shadows",
                Name = "Drop Shadows",
                Category = "Visual"
            };

            try
            {
                await Task.Run(() =>
                {
                    // Disable drop shadows
                    _backup.SetRegistryValue(
                        @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                        "ListviewShadow",
                        0,
                        RegistryValueKind.DWord);
                    
                    result.Success = true;
                });
                
                _logger.Info("Drop shadows disabled");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> EnableShadowsAsync()
        {
            var result = new OptimizationResult
            {
                Id = "visual_shadows",
                Name = "Drop Shadows",
                Category = "Visual"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.RestoreRegistryValue(
                        @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                        "ListviewShadow");
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

        // ========== BEST PERFORMANCE PRESET ==========

        private async Task<OptimizationResult> ApplyBestPerformanceAsync()
        {
            var result = new OptimizationResult
            {
                Id = "visual_performance",
                Name = "Best Performance Visual Preset",
                Category = "Visual"
            };

            try
            {
                await Task.Run(() =>
                {
                    // Set visual effects to "Adjust for best performance"
                    // UserPreferencesMask controls many UI effects
                    // Value for "Best Performance": 90 12 01 80 (hex)
                    // Value for "Best Appearance": 9E 3E 07 80 12 01 00 00
                    // We use a balanced approach that keeps important effects
                    
                    _backup.SetRegistryValue(
                        @"HKCU\Control Panel\Desktop",
                        "UserPreferencesMask",
                        new byte[] { 0x90, 0x12, 0x01, 0x80, 0x10, 0x00, 0x00, 0x00 },
                        RegistryValueKind.Binary);
                    
                    // Disable Peek
                    _backup.SetRegistryValue(
                        @"HKCU\SOFTWARE\Microsoft\Windows\DWM",
                        "EnableAeroPeek",
                        0,
                        RegistryValueKind.DWord);
                    
                    // Disable live previews
                    _backup.SetRegistryValue(
                        @"HKCU\SOFTWARE\Microsoft\Windows\DWM",
                        "AlwaysHibernateThumbnails",
                        0,
                        RegistryValueKind.DWord);
                    
                    result.Success = true;
                });
                
                _logger.Info("Best performance visual preset applied");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> RevertVisualEffectsAsync()
        {
            var result = new OptimizationResult
            {
                Id = "visual_performance",
                Name = "Best Performance Visual Preset",
                Category = "Visual"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.RestoreRegistryValue(
                        @"HKCU\Control Panel\Desktop",
                        "UserPreferencesMask");
                    
                    _backup.RestoreRegistryValue(
                        @"HKCU\SOFTWARE\Microsoft\Windows\DWM",
                        "EnableAeroPeek");
                    
                    _backup.RestoreRegistryValue(
                        @"HKCU\SOFTWARE\Microsoft\Windows\DWM",
                        "AlwaysHibernateThumbnails");
                    
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

        private bool IsTransparencyDisabled()
        {
            var value = _backup.GetRegistryValue(
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "EnableTransparency");
            return value != null && (int)value == 0;
        }

        private bool IsAnimationsDisabled()
        {
            var value = _backup.GetRegistryValue(
                @"HKCU\Control Panel\Desktop\WindowMetrics",
                "MinAnimate");
            return value != null && value.ToString() == "0";
        }

        private bool IsShadowsDisabled()
        {
            var value = _backup.GetRegistryValue(
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "ListviewShadow");
            return value != null && (int)value == 0;
        }

        private bool IsVisualEffectsOptimized()
        {
            var peek = _backup.GetRegistryValue(
                @"HKCU\SOFTWARE\Microsoft\Windows\DWM",
                "EnableAeroPeek");
            return peek != null && (int)peek == 0;
        }
    }
}
