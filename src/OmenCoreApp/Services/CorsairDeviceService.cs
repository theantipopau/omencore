using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OmenCore.Corsair;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class CorsairDeviceService
    {
        private readonly LoggingService _logging;
        private readonly ObservableCollection<CorsairDevice> _devices = new();
        public ReadOnlyObservableCollection<CorsairDevice> Devices { get; }

        public CorsairDeviceService(LoggingService logging)
        {
            _logging = logging;
            Devices = new ReadOnlyObservableCollection<CorsairDevice>(_devices);
        }

        public void Discover()
        {
            _devices.Clear();
            // TODO: Hook Corsair iCUE SDK (CUESDK.x64_2013.dll) to enumerate actual devices.
            // No devices added - only detect real connected hardware
            _logging.Info($"Discovered {_devices.Count} Corsair device(s)");
        }

        public void ApplyLightingPreset(CorsairDevice device, CorsairLightingPreset preset)
        {
            _logging.Info($"Corsair lighting -> {device.Name} uses {preset.Name}");
        }

        public void ApplyDpiStages(CorsairDevice device, IEnumerable<CorsairDpiStage> stages)
        {
            device.DpiStages = stages.ToList();
            _logging.Info($"Updated DPI stages for {device.Name}");
        }

        public void ApplyMacroProfile(CorsairDevice device, MacroProfile macro)
        {
            _logging.Info($"Applied macro '{macro.Name}' to {device.Name}");
        }

        public void SyncWithTheme(LightingProfile profile)
        {
            foreach (var device in _devices)
            {
                _logging.Info($"Syncing {device.Name} with theme '{profile.Name}' ({profile.Effect})");
            }
        }
    }
}
