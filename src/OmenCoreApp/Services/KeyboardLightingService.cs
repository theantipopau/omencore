using System.Collections.Generic;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class KeyboardLightingService
    {
        private readonly LoggingService _logging;

        public KeyboardLightingService(LoggingService logging)
        {
            _logging = logging;
        }

        public void ApplyProfile(LightingProfile profile)
        {
            // TODO: Call HP Omen SDK (LightingService) or OpenRGB bridge.
            _logging.Info($"Keyboard lighting -> {profile.Name} ({profile.Effect})");
        }

        public void ApplyEffect(LightingEffectType effect, string primary, string secondary, IEnumerable<string> zones, double speed)
        {
            _logging.Info($"Keyboard effect {effect} prim:{primary} sec:{secondary} speed:{speed}");
        }

        public void RestoreDefaults()
        {
            _logging.Info("Keyboard lighting reset to OEM defaults");
        }
    }
}
