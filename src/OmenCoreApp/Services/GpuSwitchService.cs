using System.Diagnostics;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class GpuSwitchService
    {
        private readonly LoggingService _logging;

        public GpuSwitchService(LoggingService logging)
        {
            _logging = logging;
        }

        public void Switch(GpuSwitchMode mode)
        {
            var arguments = mode switch
            {
                GpuSwitchMode.Integrated => "Set-OmenGpuMode -Mode iGPU",
                GpuSwitchMode.Discrete => "Set-OmenGpuMode -Mode dGPU",
                _ => "Set-OmenGpuMode -Mode Hybrid"
            };

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"{arguments}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
            _logging.Info($"GPU switching command issued -> {mode}");
        }
    }
}
