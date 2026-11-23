using System;
using System.Management;
using System.Threading;
using NvpwrControlBlackwell;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Opt-in NVIDIA laptop GPU power unlock (MAX via the driver's romOverride, CURRENT via the driver's internal
    /// RmControl call). The backend (OmenCore.NvidiaPower) comes from timmyy123's nvidia-power-control and
    /// bobshmo's Prophecy Power Unlocker (backend ported from 1.2.9), used with their permission. This class only adds OmenCore's safety
    /// wrapper: on a Victus laptop the HP performance mode is set and the fan engine paused around a CURRENT
    /// write, and the write never runs if that preparation fails.
    /// </summary>
    public sealed class NvidiaPowerService
    {
        private static readonly object Sync = new();
        private readonly PowerBackend _backend = new();
        private readonly FanService? _fans;
        private readonly LoggingService _logging;
        private readonly Lazy<bool> _isVictus = new(DetectVictus);

        public NvidiaPowerService(FanService? fans, LoggingService logging)
        {
            _fans = fans;
            _logging = logging;
            PowerBackend.CurrentWriteGuard = write => GuardCurrent(BeforeCurrentPower, write);
        }

        public sealed record Status(
            bool Supported, bool MaxReady, bool CurrentReady, string Gpu, string Reason,
            double? LiveCurrentW, double? LiveMaxW, int[] Targets, int? ExpectedStockW = null,
            bool NeedsDriverValidation = false);

        public sealed record Outcome(bool Success, string Message, bool RebootRequired);

        public Status Assess()
        {
            var c = _backend.CheckCompatibility();
            var p = _backend.GetPowerState();
            int? expectedStock = c.VbiosResolver is { StockMaxW: > 0 } ? c.VbiosResolver.StockMaxW : c.Profile?.StockPowerW;
            return new Status(c.Supported, c.MaxWritesReady, c.CurrentWritesReady, c.GpuName, c.Reason,
                p.CurrentW, p.MaxW, c.Profile?.Targets() ?? Array.Empty<int>(), expectedStock,
                NeedsDriverValidation: c.Profile != null && c.Driver is { CandidateFound: true, Trusted: false });
        }

        public Outcome ApplyMax(int watts) => Run("MAX " + watts + " W", () => _backend.SetMaxOverride(watts));
        /// <summary>Reads the VBIOS power table so MAX can be written. Uses nvflash64.exe if the user placed it next to OmenCore.</summary>
        public Outcome ResolveVbiosAuto() => Run("resolve VBIOS", () => _backend.TryAutoResolveVbios(out _));
        public Outcome ResolveVbiosFromRom(string romPath) => Run("resolve VBIOS from file", () => _backend.ResolveVbiosFromRom(romPath));
        public Outcome RemoveMax() => Run("remove MAX override", () => _backend.RemoveMaxOverride());
        /// <summary>
        /// Unlocks CURRENT on a driver other than the reviewed 617.14: sends a no-op that replays the driver's own
        /// request and checks nothing changed (Prophecy 1.2.9 rule). Goes through the same Victus guard as CURRENT.
        /// </summary>
        public Outcome ValidateDriver() => Run("validate driver", () => _backend.ValidateDriverResolver());
        public Outcome ApplyCurrent(int watts) => Run("CURRENT " + watts + " W", () => _backend.SetCurrent(watts));

        private Outcome Run(string what, Func<OperationResult> op)
        {
            try
            {
                var r = op();
                _logging.Info($"NVIDIA power: {what} -> {(r.Success ? "ok" : "refused")}: {r.Message}");
                return new Outcome(r.Success, r.Message, r.RebootRequired);
            }
            catch (InvalidOperationException ex)
            {
                // Raised by the Victus guard: the write was never sent.
                _logging.Warn($"NVIDIA power: {what} not sent: {ex.Message}");
                return new Outcome(false, ex.Message, false);
            }
        }

        /// <summary>Runs <paramref name="write"/> only after <paramref name="prepare"/> succeeded; releases afterwards.</summary>
        internal static int GuardCurrent(Func<IDisposable> prepare, Func<int> write)
        {
            using var lease = prepare();
            return write();
        }

        /// <summary>
        /// The HP mode byte that means "Performance" depends on the firmware's thermal policy: V0 firmware uses the legacy
        /// value (0x01), V1 and newer use 0x31 (the same value the rest of OmenCore sends on those boards). 4.4.5 always sent
        /// the legacy value, inherited from the Prophecy fork; bobshmo's standalone 1.2.0 switched to choosing by policy.
        /// </summary>
        internal static HpWmiBios.FanMode HpPerformanceModeFor(HpWmiBios.ThermalPolicyVersion policy) =>
            policy == HpWmiBios.ThermalPolicyVersion.V0 ? HpWmiBios.FanMode.LegacyPerformance : HpWmiBios.FanMode.Performance;

        private IDisposable BeforeCurrentPower()
        {
            if (!_isVictus.Value) return NoLease.Instance;

            Monitor.Enter(Sync);
            FanWriteLease? fanLease = null;
            try
            {
                if (FanService.IsAnyDiagnosticModeActive)
                    throw new InvalidOperationException("Finish OmenCore fan diagnostics before changing GPU CURRENT.");

                var maxPreset = _fans?.ActivePreset?.Mode == FanMode.Max ? _fans.ActivePreset : null;
                if (_fans != null && maxPreset == null &&
                    !_fans.ApplyPreset(new FanPreset { Name = "Performance", Mode = FanMode.Performance, IsBuiltIn = true }, immediate: true))
                    throw new InvalidOperationException("OmenCore could not prepare performance cooling. CURRENT was not sent.");

                using var hp = new HpWmiBios(_logging);
                if (!hp.IsAvailable)
                    throw new InvalidOperationException("HP WMI is unavailable (" + hp.Status + "). CURRENT was not sent.");
                var hpMode = HpPerformanceModeFor(hp.ThermalPolicy);
                if (!hp.SetFanMode(hpMode))
                    throw new InvalidOperationException($"HP performance mode ({hpMode}) was refused. CURRENT was not sent.");

                // Changing the HP mode can drop Max; give it back through the fan service (which owns its release).
                if (maxPreset != null && (!_fans!.ApplyPreset(maxPreset, immediate: true) || hp.GetFanMax() != true))
                    throw new InvalidOperationException("Fan Max could not be preserved. CURRENT was not sent.");

                if (_fans != null)
                {
                    fanLease = _fans.AcquireFanWriteLease("NVIDIA power write");
                }
                return new HeldLease(fanLease);
            }
            catch
            {
                fanLease?.Dispose();
                Monitor.Exit(Sync);
                throw;
            }
        }

        private static bool DetectVictus()
        {
            try
            {
                using var q = new ManagementObjectSearcher("SELECT Manufacturer,Model FROM Win32_ComputerSystem");
                foreach (ManagementObject o in q.Get())
                    if ((Convert.ToString(o["Manufacturer"]) ?? "").Contains("HP", StringComparison.OrdinalIgnoreCase) &&
                        (Convert.ToString(o["Model"]) ?? "").Contains("Victus", StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch (ManagementException)
            {
                // Unknown host: treated as not Victus, so the plain (no HP preparation) path is used.
            }
            return false;
        }

        private sealed class NoLease : IDisposable
        {
            public static readonly NoLease Instance = new();
            public void Dispose() { }
        }

        private sealed class HeldLease : IDisposable
        {
            private readonly FanWriteLease? _fanLease;
            public HeldLease(FanWriteLease? fanLease) { _fanLease = fanLease; }

            public void Dispose()
            {
                try { _fanLease?.Dispose(); }
                finally { Monitor.Exit(Sync); }
            }
        }
    }
}
