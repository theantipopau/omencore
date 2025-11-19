using System.Collections.Generic;
using OmenCore.Corsair;
using OmenCore.Models;

namespace OmenCore.Services
{
    public static class DefaultConfiguration
    {
        public static AppConfig Create()
        {
            var config = new AppConfig();

            config.FanPresets.AddRange(new[]
            {
                new FanPreset
                {
                    Name = "Quiet",
                    IsBuiltIn = true,
                    Curve = new List<FanCurvePoint>
                    {
                        new() { TemperatureC = 40, FanPercent = 20 },
                        new() { TemperatureC = 70, FanPercent = 45 },
                        new() { TemperatureC = 85, FanPercent = 65 }
                    }
                },
                new FanPreset
                {
                    Name = "Balanced",
                    IsBuiltIn = true,
                    Curve = new List<FanCurvePoint>
                    {
                        new() { TemperatureC = 40, FanPercent = 30 },
                        new() { TemperatureC = 70, FanPercent = 60 },
                        new() { TemperatureC = 90, FanPercent = 80 }
                    }
                },
                new FanPreset
                {
                    Name = "Performance",
                    IsBuiltIn = true,
                    Curve = new List<FanCurvePoint>
                    {
                        new() { TemperatureC = 40, FanPercent = 40 },
                        new() { TemperatureC = 70, FanPercent = 75 },
                        new() { TemperatureC = 95, FanPercent = 100 }
                    }
                },
                new FanPreset
                {
                    Name = "Max",
                    IsBuiltIn = true,
                    Curve = new List<FanCurvePoint>
                    {
                        new() { TemperatureC = 0, FanPercent = 100 },
                        new() { TemperatureC = 100, FanPercent = 100 }
                    }
                }
            });

            config.PerformanceModes.AddRange(new[]
            {
                new PerformanceMode { Name = "Quiet", CpuPowerLimitWatts = 25, GpuPowerLimitWatts = 45, LinkedPowerPlanGuid = "381b4222-f694-41f0-9685-ff5bb260df2e", Description = "Low-noise daily use" },
                new PerformanceMode { Name = "Balanced", CpuPowerLimitWatts = 45, GpuPowerLimitWatts = 85, LinkedPowerPlanGuid = "381b4222-f694-41f0-9685-ff5bb260df2e", Description = "Default OEM behaviour" },
                new PerformanceMode { Name = "Performance", CpuPowerLimitWatts = 65, GpuPowerLimitWatts = 115, LinkedPowerPlanGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", Description = "Sustained gaming" },
                new PerformanceMode { Name = "Turbo", CpuPowerLimitWatts = 80, GpuPowerLimitWatts = 140, LinkedPowerPlanGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", Description = "Bench / burst" }
            });

            config.SystemToggles.AddRange(new[]
            {
                new ServiceToggle { Name = "Desktop Window Manager", ServiceName = "UxSms", EnabledByDefault = true, Description = "Windows animations" },
                new ServiceToggle { Name = "Xbox Services", ServiceName = "XblGameSave", EnabledByDefault = false, Description = "Game bar background" },
                new ServiceToggle { Name = "HP Omen Background", ServiceName = "OmenCommand", EnabledByDefault = true, Description = "OEM telemetry" }
            });

            config.LightingProfiles.AddRange(new[]
            {
                new LightingProfile { Name = "Omen Red", Effect = LightingEffectType.Static, PrimaryColor = "#E6002E", Zones = new List<string> { "All" } },
                new LightingProfile { Name = "Breathe", Effect = LightingEffectType.Breathing, PrimaryColor = "#E6002E", SecondaryColor = "#1F8FFF", Zones = new List<string> { "Left", "Center", "Right" }, Speed = 0.8 },
                new LightingProfile { Name = "Wave", Effect = LightingEffectType.Wave, PrimaryColor = "#FF6600", SecondaryColor = "#0066FF", Zones = new List<string> { "All" }, Speed = 1.4 }
            });

            config.CorsairLightingPresets.AddRange(new[]
            {
                new CorsairLightingPreset { Name = "Sync Static", Effect = LightingEffectType.Static, PrimaryColor = "#E6002E" },
                new CorsairLightingPreset { Name = "Sync Wave", Effect = LightingEffectType.Wave, PrimaryColor = "#E6002E", SecondaryColor = "#00FFFF" }
            });

            config.DefaultCorsairDpi.AddRange(new[]
            {
                new CorsairDpiStage { Name = "Sniper", Dpi = 800, LiftOffDistanceMm = 1.0 },
                new CorsairDpiStage { Name = "Default", Dpi = 1600, IsDefault = true, LiftOffDistanceMm = 1.5 },
                new CorsairDpiStage { Name = "Turbo", Dpi = 3200, LiftOffDistanceMm = 2.0 }
            });

            config.MacroProfiles.Add(new MacroProfile { Name = "Sample", Actions = new List<MacroAction>() });

            config.EcFanRegisterMap["CPU"] = 0x2F;
            config.EcFanRegisterMap["GPU"] = 0x30;

            config.Undervolt = new UndervoltPreferences
            {
                DefaultOffset = new UndervoltOffset { CoreMv = -90, CacheMv = -60 },
                RespectExternalControllers = true,
                ProbeIntervalMs = 4000
            };

            config.Monitoring = new MonitoringPreferences
            {
                PollIntervalMs = 1500,
                HistoryCount = 120,
                LowOverheadMode = false
            };

            return config;
        }
    }
}
