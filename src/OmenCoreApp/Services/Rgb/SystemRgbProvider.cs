using System.Threading.Tasks;
using OmenCore.Services;

namespace OmenCore.Services.Rgb
{
    /// <summary>
    /// Composite provider that applies effects to all available providers registered with the RgbManager.
    /// Useful as a "generic" entrypoint for controlling desktop RGB broadly.
    /// </summary>
    public class SystemRgbProvider : IRgbProvider
    {
        private readonly RgbManager _manager;
        private readonly LoggingService _logging;

        public string ProviderName => "SystemGeneric";
        public bool IsAvailable { get; private set; } = false;

        public SystemRgbProvider(RgbManager manager, LoggingService logging)
        {
            _manager = manager;
            _logging = logging;
        }

        public Task InitializeAsync()
        {
            // Consider available if any registered provider reports availability
            IsAvailable = false;
            foreach (var p in _manager.Providers)
            {
                if (p.IsAvailable) { IsAvailable = true; break; }
            }
            _logging.Info($"SystemRgbProvider initialized, available={IsAvailable}");
            return Task.CompletedTask;
        }

        public Task ApplyEffectAsync(string effectId)
        {
            // Apply to all providers via RgbManager
            return _manager.ApplyEffectToAllAsync(effectId);
        }
    }
}