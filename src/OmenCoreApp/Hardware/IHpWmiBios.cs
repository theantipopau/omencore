using System;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Minimal interface to abstract HP WMI BIOS interactions used by fan controller.
    /// Allows unit tests to inject a fake implementation.
    /// </summary>
    public interface IHpWmiBios
    {
        bool IsAvailable { get; }
        string Status { get; }
        HpWmiBios.ThermalPolicyVersion ThermalPolicy { get; }
        int FanCount { get; }
        int MaxFanLevel { get; }

        (int fan1Rpm, int fan2Rpm)? GetFanRpmDirect();
        (byte fan1, byte fan2)? GetFanLevel();

        bool SetFanMax(bool enabled);
        bool SetFanLevel(byte fan1, byte fan2);
        bool SetFanMode(HpWmiBios.FanMode mode);

        double? GetTemperature();
        double? GetGpuTemperature();
        void ExtendFanCountdown();

        (bool customTgp, bool ppab, int dState)? GetGpuPower();
        bool SetGpuPower(HpWmiBios.GpuPowerLevel level);
        HpWmiBios.GpuMode? GetGpuMode();

        void Dispose();
    }
}
