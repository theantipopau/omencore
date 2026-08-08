using System;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Models;

namespace OmenCore.Hardware
{
    /// <summary>
    /// AMD Ryzen Curve Optimizer undervolting provider.
    /// Based on G-Helper/UXTU implementation.
    /// </summary>
    public class AmdUndervoltProvider : ICpuUndervoltProvider, IDisposable
    {
        private const string RyzenAi9ExperimentalMessage = "Ryzen AI 9 Curve Optimizer path is experimental. Use conservative offsets and verify stability.";
        private readonly object _stateLock = new();
        private readonly RyzenSmu _smu;
        private readonly RyzenCpuInfo _cpuInfo;
        
        private int _lastAllCoreCO;
        private int _lastIgpuCO;
        private bool _disposed;

        public string ActiveBackend { get; private set; } = "None";
        public bool IsSupported => _cpuInfo.SupportsUndervolt;
        public bool SupportsIgpu => _cpuInfo.SupportsIgpuUndervolt;
        public RyzenFamily Family => _cpuInfo.Family;
        public string CpuName => _cpuInfo.CpuName;

        public AmdUndervoltProvider()
        {
            _cpuInfo = RyzenControl.GetCpuInfo();
            _smu = new RyzenSmu();

            if (_smu.Initialize())
            {
                RyzenControl.ConfigureSmuAddresses(_smu);
                ActiveBackend = "PawnIO (SMU)";
            }
        }

        /// <summary>
        /// Apply undervolt using Intel-style offset model.
        /// Maps Core offset to All-Core Curve Optimizer.
        /// </summary>
        public Task ApplyOffsetAsync(UndervoltOffset offset, CancellationToken token)
        {
            var safeOffset = TuningGuardrails.ClampCpuUndervoltOffset(offset, amdCurveOptimizer: true);

            // Convert Intel-style mV offset to Curve Optimizer units
            // CO is roughly 3-5mV per count, we'll approximate
            int coCounts = (int)(safeOffset.CoreMv / 4.0);
            int igpuCoCounts = SupportsIgpu ? (int)(safeOffset.CacheMv / 4.0) : 0;

            return ApplyRyzenOffsetAsync(coCounts, igpuCoCounts, token);
        }

        /// <summary>
        /// Apply AMD-native Curve Optimizer offset.
        /// </summary>
        public Task ApplyRyzenOffsetAsync(int allCoreCO, int igpuCO, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (_stateLock)
            {
                if (!_smu.IsAvailable)
                {
                    throw new InvalidOperationException("Ryzen SMU is not available. Install PawnIO driver.");
                }

                if (!_cpuInfo.SupportsUndervolt)
                {
                    throw new InvalidOperationException($"CPU {_cpuInfo.CpuName} does not support Curve Optimizer undervolting.");
                }

                // Apply All-Core CO
                var status = SetAllCoreCO(allCoreCO);
                if (status != RyzenSmu.SmuStatus.Ok)
                {
                    string hint = status == RyzenSmu.SmuStatus.Bad
                        ? $"Failed to set All-Core CO (status: {status}). The SMU did not respond — this usually means " +
                          $"the HP BIOS has restricted Curve Optimizer access on this model, or OmenCore is not running " +
                          $"as Administrator. Ensure PawnIO is installed and OmenCore is launched with admin rights."
                        : $"Failed to set All-Core CO: {status}";
                    throw new InvalidOperationException(hint);
                }
                _lastAllCoreCO = allCoreCO;

                // Apply iGPU CO if supported
                if (_cpuInfo.SupportsIgpuUndervolt && igpuCO != 0)
                {
                    status = SetIgpuCO(igpuCO);
                    if (status != RyzenSmu.SmuStatus.Ok)
                    {
                        // iGPU CO failure is non-fatal
                    }
                    _lastIgpuCO = igpuCO;
                }
            }

            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (_stateLock)
            {
                if (_smu.IsAvailable && _cpuInfo.SupportsUndervolt)
                {
                    try
                    {
                        SetAllCoreCO(0);
                        if (_cpuInfo.SupportsIgpuUndervolt)
                        {
                            SetIgpuCO(0);
                        }
                    }
                    catch
                    {
                        // Ignore reset failures
                    }
                }

                _lastAllCoreCO = 0;
                _lastIgpuCO = 0;
            }

            return Task.CompletedTask;
        }

        public Task<UndervoltStatus> ProbeAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            lock (_stateLock)
            {
                var status = new UndervoltStatus
                {
                    // Convert CO to approximate mV (CO * ~4mV)
                    CurrentCoreOffsetMv = _lastAllCoreCO * 4,
                    CurrentCacheOffsetMv = _lastIgpuCO * 4, // Use cache field for iGPU
                    IsRuntimeReady = _smu.IsAvailable && _cpuInfo.SupportsUndervolt,
                    ControlledByOmenCore = true,
                    Timestamp = DateTime.Now
                };

                if (!_smu.IsAvailable)
                {
                    status.IsRuntimeReady = false;
                    status.RuntimeBlockReason = "AMD SMU backend is unavailable. Install PawnIO, run OmenCore as administrator, and reboot if the driver was just installed.";
                    status.Warning = status.RuntimeBlockReason;
                    status.ControlledByOmenCore = false;
                }
                else if (RyzenControl.IsRyzenAi9CurveOptimizerUnsupported())
                {
                    status.Warning = RyzenAi9ExperimentalMessage;
                }
                else if (!_cpuInfo.SupportsUndervolt)
                {
                    status.IsRuntimeReady = false;
                    status.RuntimeBlockReason = $"CPU {_cpuInfo.CpuName} does not expose a supported Curve Optimizer control path on this firmware.";
                    status.Warning = status.RuntimeBlockReason;
                    status.ControlledByOmenCore = false;
                }
                else if (_cpuInfo.Family == RyzenFamily.Unknown)
                {
                    status.IsRuntimeReady = false;
                    status.RuntimeBlockReason = "Unknown AMD CPU family. SMU command addresses cannot be validated safely for Curve Optimizer writes.";
                    status.Warning = status.RuntimeBlockReason;
                    status.ControlledByOmenCore = false;
                }

                return Task.FromResult(status);
            }
        }

        /// <summary>
        /// Set All-Core Curve Optimizer offset.
        /// Negative values = undervolt.
        /// </summary>
        private RyzenSmu.SmuStatus SetAllCoreCO(int value)
        {
            // Safety clamp: AMD Curve Optimizer safe range is -30 to +30
            value = Math.Clamp(value, -30, 30);

            // Convert signed offset to SMU format
            // Formula from G-Helper: 0x100000 - (uint)(-1 * value) for negative values
            uint uvalue = value < 0 
                ? (uint)(0x100000 - (uint)(-value))
                : (uint)value;

            uint[] args = new uint[6];
            args[0] = uvalue;
            RyzenSmu.SmuStatus result = RyzenSmu.SmuStatus.Failed;

            switch (_cpuInfo.Family)
            {
                case RyzenFamily.RenoirLucienne:
                case RyzenFamily.CezanneBarcelo:
                    result = _smu.SendMp1(0x55, ref args);
                    if (result == RyzenSmu.SmuStatus.Ok)
                        result = _smu.SendPsmu(0xB1, ref args);
                    break;

                case RyzenFamily.Matisse:
                case RyzenFamily.Vermeer:
                    result = _smu.SendMp1(0x36, ref args);
                    if (result == RyzenSmu.SmuStatus.Ok)
                        result = _smu.SendPsmu(0xB, ref args);
                    break;

                case RyzenFamily.VanGogh:
                case RyzenFamily.Rembrandt:
                case RyzenFamily.Phoenix:
                case RyzenFamily.Mendocino:
                case RyzenFamily.HawkPoint:
                    result = _smu.SendPsmu(0x5D, ref args);
                    if (result != RyzenSmu.SmuStatus.Ok)
                    {
                        // Some HP Victus/Phoenix 2 variants (Model 0x78) route CO
                        // through MP1 rather than PSMU — try that as a fallback.
                        uint[] mp1Args = (uint[])args.Clone();
                        var mp1Result = _smu.SendMp1(0x5D, ref mp1Args);
                        if (mp1Result == RyzenSmu.SmuStatus.Ok)
                            result = mp1Result;
                    }
                    break;

                // Strix Point takes All-Core CO on MP1 0x4C, matching RyzenAdj's set_coall
                // (lib/api.c, FAM_STRIXPOINT). Previously grouped with Phoenix above and sent
                // PSMU 0x5D.
                //
                // Measured on board 8D87 / Ryzen AI 9 HX 375 (family 1Ah model 24h) with
                // tools/SmuProbe --outcome: CO -25 raises sustained all-core clock by
                // +4.9% (3148 -> 3301 MHz, mean of 3 alternating pairs, spread 4.8-4.9%)
                // against a sham control of +/-0.1%. The offset toggles cleanly - clock
                // returns to baseline every time it is removed.
                //
                // 0x5D was NOT the reason Curve Optimizer did not work here: it is also
                // accepted on this part. The transport was - RyzenSmu loaded no PawnIO module
                // at all and called PCI-config ioctls that no bundled module exports. 0x4C is
                // used because RyzenAdj specifies it for FAM_STRIXPOINT and it is therefore the
                // better-supported path, not because 0x5D was measured to fail.
                case RyzenFamily.StrixPoint:
                    result = _smu.SendMp1(0x4C, ref args);
                    break;

                // Left as-is deliberately. RyzenAdj's set_coall sends only MP1 0x4C here, with
                // no PSMU follow-up, so requiring the 0x5D call to also succeed may report a
                // working CO write as failed. Not changed without Strix Halo hardware to test
                // on - that is a different part (Ryzen AI MAX) than the one fixed above.
                case RyzenFamily.StrixHalo:
                    result = _smu.SendMp1(0x4C, ref args);
                    if (result == RyzenSmu.SmuStatus.Ok)
                        result = _smu.SendPsmu(0x5D, ref args);
                    break;

                case RyzenFamily.RaphaelDragonRange:
                case RyzenFamily.FireRange:
                    result = _smu.SendPsmu(0x7, ref args);
                    break;

                case RyzenFamily.Zen1Plus:
                case RyzenFamily.Raven:
                case RyzenFamily.Picasso:
                case RyzenFamily.Dali:
                    // Older architectures don't support Curve Optimizer
                    result = RyzenSmu.SmuStatus.UnknownCmd;
                    break;

                default:
                    // Unknown family - try Phoenix/Rembrandt command as fallback
                    result = _smu.SendPsmu(0x5D, ref args);
                    break;
            }

            return result;
        }

        /// <summary>
        /// Set iGPU Curve Optimizer offset (for APUs).
        /// </summary>
        private RyzenSmu.SmuStatus SetIgpuCO(int value)
        {
            // Safety clamp: AMD Curve Optimizer safe range is -30 to +30
            value = Math.Clamp(value, -30, 30);

            uint uvalue = value < 0
                ? (uint)(0x100000 - (uint)(-value))
                : (uint)value;

            uint[] args = new uint[6];
            args[0] = uvalue;
            RyzenSmu.SmuStatus result = RyzenSmu.SmuStatus.Failed;

            switch (_cpuInfo.Family)
            {
                case RyzenFamily.RenoirLucienne:
                case RyzenFamily.CezanneBarcelo:
                    result = _smu.SendMp1(0x64, ref args);
                    if (result == RyzenSmu.SmuStatus.Ok)
                        result = _smu.SendPsmu(0x57, ref args);
                    break;

                case RyzenFamily.VanGogh:
                case RyzenFamily.Rembrandt:
                case RyzenFamily.Phoenix:
                case RyzenFamily.Mendocino:
                case RyzenFamily.HawkPoint:
                case RyzenFamily.StrixPoint:
                case RyzenFamily.StrixHalo:
                    result = _smu.SendPsmu(0xB7, ref args);
                    break;
            }

            return result;
        }

        /// <summary>
        /// Set STAPM (sustained power) limit in mW.
        ///
        /// Restricted to <see cref="RyzenFamily.StrixPoint"/>. The transport fix that made this
        /// method's SMU writes actually reach the silicon (previously the module never loaded,
        /// so every call here was a guaranteed no-op regardless of family) also newly activates
        /// it for every other family with a configured mailbox address below - eleven of them,
        /// none with field evidence. Unlike Curve Optimizer (undervolt only, self-limiting,
        /// clamped +-30 and the thing this transport fix was explicitly about), this is a power
        /// LIMIT increase - the same class of write this project already treats as needing
        /// field validation before shipping (see PowerLimitController/SupportsEcPowerLimits).
        /// Remove this gate only once a specific family has been measured, the way Strix Point
        /// was.
        /// </summary>
        public RyzenSmu.SmuStatus SetStapmLimit(uint valueMw)
        {
            if (_cpuInfo.Family != RyzenFamily.StrixPoint)
            {
                return RyzenSmu.SmuStatus.Failed;
            }

            valueMw = Math.Clamp(valueMw, 15_000u, 54_000u);

            uint[] args = new uint[6];
            args[0] = valueMw;
            RyzenSmu.SmuStatus result = RyzenSmu.SmuStatus.Failed;

            switch (_cpuInfo.Family)
            {
                case RyzenFamily.Raven:
                case RyzenFamily.Picasso:
                case RyzenFamily.Dali:
                    result = _smu.SendMp1(0x1A, ref args);
                    break;

                case RyzenFamily.RenoirLucienne:
                case RyzenFamily.VanGogh:
                case RyzenFamily.CezanneBarcelo:
                case RyzenFamily.Rembrandt:
                case RyzenFamily.Phoenix:
                case RyzenFamily.Mendocino:
                case RyzenFamily.HawkPoint:
                case RyzenFamily.StrixPoint:
                    result = _smu.SendMp1(0x14, ref args);
                    if (result == RyzenSmu.SmuStatus.Ok)
                        result = _smu.SendPsmu(0x31, ref args);
                    break;
            }

            return result;
        }

        /// <summary>
        /// Set temperature limit in degrees Celsius.
        ///
        /// Restricted to <see cref="RyzenFamily.StrixPoint"/> for the same reason as
        /// <see cref="SetStapmLimit"/>: the transport fix newly activates real writes on
        /// seventeen configured families, none with field evidence, and this is a CPU thermal
        /// limit increase, not the Curve Optimizer change this fix was actually about.
        /// </summary>
        public RyzenSmu.SmuStatus SetTctlTemp(uint tempC)
        {
            if (_cpuInfo.Family != RyzenFamily.StrixPoint)
            {
                return RyzenSmu.SmuStatus.Failed;
            }

            tempC = Math.Clamp(tempC, 75u, 105u);

            uint[] args = new uint[6];
            args[0] = tempC;
            RyzenSmu.SmuStatus result = RyzenSmu.SmuStatus.Failed;

            switch (_cpuInfo.Family)
            {
                case RyzenFamily.Zen1Plus:
                    result = _smu.SendPsmu(0x68, ref args);
                    break;

                case RyzenFamily.Raven:
                case RyzenFamily.Picasso:
                case RyzenFamily.Dali:
                    result = _smu.SendMp1(0x1F, ref args);
                    break;

                case RyzenFamily.RenoirLucienne:
                case RyzenFamily.VanGogh:
                case RyzenFamily.CezanneBarcelo:
                case RyzenFamily.Rembrandt:
                case RyzenFamily.Phoenix:
                case RyzenFamily.Mendocino:
                case RyzenFamily.HawkPoint:
                case RyzenFamily.StrixPoint:
                case RyzenFamily.StrixHalo:
                    result = _smu.SendMp1(0x19, ref args);
                    break;

                case RyzenFamily.Matisse:
                case RyzenFamily.Vermeer:
                    result = _smu.SendMp1(0x23, ref args);
                    if (result == RyzenSmu.SmuStatus.Ok)
                        result = _smu.SendPsmu(0x56, ref args);
                    break;

                case RyzenFamily.RaphaelDragonRange:
                case RyzenFamily.FireRange:
                    result = _smu.SendMp1(0x3F, ref args);
                    if (result == RyzenSmu.SmuStatus.Ok)
                        result = _smu.SendPsmu(0x59, ref args);
                    break;
            }

            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _smu.Dispose();
        }
    }
}
