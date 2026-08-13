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

        /// <summary>
        /// True when the most recent <see cref="ApplyOffsetAsync"/> call was asked for per-core
        /// offsets that this backend has no write path for. The AMD Curve Optimizer path only
        /// ever applies an all-core value; there is no per-core SMU message implemented.
        /// See docs/TUNING-SUBSYSTEMS-REVIEW.md, finding F1.
        /// </summary>
        private bool _lastRequestHadUnappliedPerCore;

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

            _lastRequestHadUnappliedPerCore = safeOffset.HasPerCoreOffsets;

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
                _lastRequestHadUnappliedPerCore = false;
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

                if (_lastRequestHadUnappliedPerCore)
                {
                    status.PerCoreOffsetsRequestedButNotApplied = true;
                    const string perCoreWarning = "Per-core voltage offsets were requested but were not written - the AMD Curve Optimizer path only applies a single all-core value.";
                    status.Warning = string.IsNullOrEmpty(status.Warning)
                        ? perCoreWarning
                        : $"{status.Warning} {perCoreWarning}";
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
            value = Math.Clamp(value, TuningGuardrails.AmdCurveOptimizerMinCount, TuningGuardrails.AmdCurveOptimizerMaxCount);

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
            value = Math.Clamp(value, TuningGuardrails.AmdCurveOptimizerMinCount, TuningGuardrails.AmdCurveOptimizerMaxCount);

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
        /// Lowest SMU power limit this provider will ask for, in milliwatts. Below roughly this
        /// the part cannot hold its own idle floor and the limit stops being meaningful.
        /// </summary>
        internal const uint MinPowerLimitMw = 15_000;

        /// <summary>
        /// Highest SMU power limit this provider will ask for on this silicon, in milliwatts.
        /// See <see cref="RyzenControl.GetMaxPowerLimitMw()"/> for what this bound is and is not.
        /// </summary>
        public uint MaxPowerLimitMw => RyzenControl.GetMaxPowerLimitMw(_cpuInfo.Family);

        /// <summary>Lowest limit this provider will ask for, in milliwatts.</summary>
        public uint MinPowerLimitWattsFloor => MinPowerLimitMw / 1000;

        private uint ClampPowerLimit(uint valueMw) =>
            Math.Clamp(valueMw, MinPowerLimitMw, MaxPowerLimitMw);

        // SMU message IDs for the power limits, taken verbatim from RyzenAdj's lib/api.c rather
        // than inferred. Named so the citation sits next to the value and a test can pin them:
        // a silently changed message ID is the kind of thing that returns Ok and does nothing.
        internal const uint Mp1SetStapmLimit = 0x14;   // set_stapm_limit
        internal const uint PsmuSetStapmLimit = 0x31;  // set_stapm_limit, fallback on MP1 error
        internal const uint Mp1SetFastLimit = 0x15;    // set_fast_limit
        internal const uint Mp1SetSlowLimit = 0x16;    // set_slow_limit
        internal const uint Mp1SetApuSlowLimit = 0x23; // set_apu_slow_limit

        /// <summary>
        /// Whether this CPU family has confirmed message IDs for the fast and slow power limits.
        /// Only families whose IDs are taken verbatim from RyzenAdj's lib/api.c are listed; the
        /// rest return <see cref="RyzenSmu.SmuStatus.UnknownCmd"/> rather than have a
        /// plausible-looking ID guessed for them. Raven, Picasso and Dali use a different scheme.
        /// </summary>
        internal static bool FamilySupportsPptLimits(RyzenFamily family) => family switch
        {
            RyzenFamily.RenoirLucienne => true,
            RyzenFamily.VanGogh => true,
            RyzenFamily.CezanneBarcelo => true,
            RyzenFamily.Rembrandt => true,
            RyzenFamily.Phoenix => true,
            RyzenFamily.Mendocino => true,
            RyzenFamily.HawkPoint => true,
            RyzenFamily.StrixPoint => true,
            RyzenFamily.StrixHalo => true,
            _ => false
        };

        /// <summary>
        /// Whether this CPU family exposes the separate APU slow (PPT LIMIT APU) domain.
        /// RyzenAdj's set_apu_slow_limit omits Renoir, Lucienne, Cezanne, Van Gogh and
        /// Mendocino, so this is a narrower list than <see cref="FamilySupportsPptLimits"/>.
        /// </summary>
        internal static bool FamilySupportsApuSlowLimit(RyzenFamily family) => family switch
        {
            RyzenFamily.Rembrandt => true,
            RyzenFamily.Phoenix => true,
            RyzenFamily.HawkPoint => true,
            RyzenFamily.StrixPoint => true,
            RyzenFamily.StrixHalo => true,
            _ => false
        };

        private bool SupportsPptLimits => FamilySupportsPptLimits(_cpuInfo.Family);

        private bool SupportsApuSlowLimit => FamilySupportsApuSlowLimit(_cpuInfo.Family);

        /// <summary>
        /// Set STAPM (sustained power) limit in mW.
        ///
        /// Was gated to <see cref="RyzenFamily.StrixPoint"/> only earlier this cycle, since the
        /// transport fix that made this method's SMU writes actually reach the silicon (previously
        /// the module never loaded, so every call here was a guaranteed no-op) also newly activated
        /// it for every other family with a configured mailbox address below, with zero field
        /// evidence beyond Strix Point at the time. That gate was lifted once the adapter-clamp-lift
        /// feature (<see cref="ApuPowerClampService"/>) needed the other families and the project
        /// owner accepted the broader-family risk on that basis - see
        /// <see cref="ClampPowerLimit(uint)"/> for the per-family bound this now clamps to.
        /// </summary>
        public RyzenSmu.SmuStatus SetStapmLimit(uint valueMw)
        {
            valueMw = ClampPowerLimit(valueMw);

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
                    // PSMU 0x31 is a fallback for when MP1 0x14 is rejected, not a second half
                    // that also has to succeed. RyzenAdj's set_stapm_limit sends MP1 0x14 and
                    // only retries on PSMU when that errors ("Retry with PSMU" in lib/api.c).
                    //
                    // Chaining them on success instead meant the PSMU status became the return
                    // value, so a STAPM limit that MP1 had already accepted was reported as
                    // failed whenever the PSMU mailbox declined the same message - and the
                    // caller then surfaced an error for a write that did land.
                    result = SendWithPsmuFallback(Mp1SetStapmLimit, PsmuSetStapmLimit, valueMw);
                    break;
            }

            return result;
        }

        /// <summary>
        /// Send an SMU message on MP1, falling back to the PSMU mailbox only if MP1 rejects it.
        ///
        /// Each mailbox gets its own argument buffer: <see cref="RyzenSmu.SendMp1"/> takes its
        /// arguments by reference and overwrites them with the SMU's response, so reusing one
        /// buffer would send the first call's reply as the second call's payload.
        /// </summary>
        private RyzenSmu.SmuStatus SendWithPsmuFallback(uint mp1Message, uint psmuMessage, uint value)
        {
            uint[] mp1Args = new uint[6];
            mp1Args[0] = value;

            var result = _smu.SendMp1(mp1Message, ref mp1Args);
            if (result == RyzenSmu.SmuStatus.Ok)
            {
                return result;
            }

            uint[] psmuArgs = new uint[6];
            psmuArgs[0] = value;

            var fallback = _smu.SendPsmu(psmuMessage, ref psmuArgs);
            return fallback == RyzenSmu.SmuStatus.Ok ? fallback : result;
        }

        private RyzenSmu.SmuStatus SendMp1(uint message, uint value)
        {
            uint[] args = new uint[6];
            args[0] = value;
            return _smu.SendMp1(message, ref args);
        }

        /// <summary>
        /// Set the PPT fast limit (short boost budget) in mW.
        ///
        /// MP1 0x15, from RyzenAdj's set_fast_limit (lib/api.c). This is the limit the part
        /// boosts against for a few seconds before <see cref="SetSlowLimit"/> takes over, so a
        /// fast limit below the slow limit truncates boost rather than raising sustained power.
        /// </summary>
        public RyzenSmu.SmuStatus SetFastLimit(uint valueMw)
        {
            if (!SupportsPptLimits) return RyzenSmu.SmuStatus.UnknownCmd;
            return SendMp1(Mp1SetFastLimit, ClampPowerLimit(valueMw));
        }

        /// <summary>
        /// Set the PPT slow limit (sustained CPU budget) in mW.
        ///
        /// MP1 0x16, from RyzenAdj's set_slow_limit (lib/api.c).
        /// </summary>
        public RyzenSmu.SmuStatus SetSlowLimit(uint valueMw)
        {
            if (!SupportsPptLimits) return RyzenSmu.SmuStatus.UnknownCmd;
            return SendMp1(Mp1SetSlowLimit, ClampPowerLimit(valueMw));
        }

        /// <summary>
        /// Set the APU slow limit (PPT LIMIT APU) in mW.
        ///
        /// MP1 0x23, from RyzenAdj's set_apu_slow_limit (lib/api.c). Governs the whole APU
        /// package rather than the CPU PPT domain, and on parts that expose it separately it
        /// becomes the binding limit once STAPM, fast and slow are raised past it - so raising
        /// the other three without this one moves the wall rather than removing it.
        /// </summary>
        public RyzenSmu.SmuStatus SetApuSlowLimit(uint valueMw)
        {
            if (!SupportsApuSlowLimit) return RyzenSmu.SmuStatus.UnknownCmd;
            return SendMp1(Mp1SetApuSlowLimit, ClampPowerLimit(valueMw));
        }

        /// <summary>
        /// Apply STAPM, fast, slow and APU-slow together, which is how they are meant to move:
        /// raising one alone just hands the ceiling to the next.
        ///
        /// A zero value in <paramref name="limits"/> means "leave this one alone". Every limit
        /// is attempted regardless of whether the ones before it succeeded - they are
        /// independent SMU messages and a part that refuses one may well accept the others.
        ///
        /// The returned statuses say the mailbox accepted each message. They are NOT evidence
        /// that the limits are in force: this SMU returns Ok for messages that change nothing,
        /// and there is no readback on the PawnIO path to check against. Confirm with
        /// tools/SmuProbe --limits, which reads the PM table over an independent transport.
        ///
        /// NOR ARE THEY DURABLE. Observed on board 8D87: limits written here held for the whole
        /// of a sustained load and read back exactly, then the platform took them back once the
        /// load ended - the four limits later read 71/71/60/45 W, which nothing here wrote, and
        /// whose 60 W is exactly this board's NVPCF ATPP value. The ACPI power path pushes its
        /// own numbers into the same registers. A caller that needs a limit to persist has to
        /// re-assert it; treating one call as set-and-forget will silently drift back.
        ///
        /// TDC and EDC (vrm-current / vrmmax-current) are deliberately absent. A power limit is
        /// self-limiting - ask for more watts than the cooling or the supply can deliver and you
        /// get throttling, and a reboot clears it. A current limit governs how hard the VRM is
        /// driven, sustained overcurrent is not self-correcting, and "a reboot fixes it" stops
        /// being true. Different risk class, and not one this file should open by default.
        /// </summary>
        public AmdPowerLimitReport ApplyPowerLimits(RyzenPowerLimits limits)
        {
            if (limits is null) throw new ArgumentNullException(nameof(limits));

            lock (_stateLock)
            {
                if (!_smu.IsAvailable)
                {
                    throw new InvalidOperationException(
                        "Ryzen SMU is not available. Install PawnIO driver and run as Administrator.");
                }

                return new AmdPowerLimitReport
                {
                    Stapm = ApplyOne(limits.StapmLimit, SetStapmLimit),
                    Fast = ApplyOne(limits.FastLimit, SetFastLimit),
                    Slow = ApplyOne(limits.SlowLimit, SetSlowLimit),
                    ApuSlow = ApplyOne(limits.ApuSlowLimit, SetApuSlowLimit)
                };
            }
        }

        private AmdPowerLimitStep ApplyOne(uint requestedMw, Func<uint, RyzenSmu.SmuStatus> setter)
        {
            if (requestedMw == 0)
            {
                return new AmdPowerLimitStep { Requested = false };
            }

            uint clamped = ClampPowerLimit(requestedMw);
            return new AmdPowerLimitStep
            {
                Requested = true,
                RequestedMw = clamped,
                WasClamped = clamped != requestedMw,
                Status = setter(clamped)
            };
        }

        /// <summary>
        /// Set temperature limit in degrees Celsius.
        ///
        /// Restricted to <see cref="RyzenFamily.StrixPoint"/> for the same reason as
        /// <see cref="SetStapmLimit"/>: the transport fix newly activates real writes on
        /// seventeen configured families, none with field evidence, and this is a CPU thermal
        /// limit increase, not the Curve Optimizer change this fix was actually about.
        ///
        /// MP1 0x19 is the message StrixPoint (and every other currently-gated family, per
        /// RyzenAdj's set_tctl_temp) uses - listed here only for the one family this method
        /// actually reaches. A per-family table for the other sixteen was removed since the
        /// early-return above made it permanently unreachable dead code (docs/TUNING-SUBSYSTEMS-REVIEW.md,
        /// finding F5); re-add entries only alongside lifting the gate for that specific family,
        /// with its own message ID confirmed rather than copied from this switch.
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
            return _smu.SendMp1(0x19, ref args);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _smu.Dispose();
        }
    }

    /// <summary>
    /// What one SMU power-limit message did.
    /// </summary>
    public class AmdPowerLimitStep
    {
        /// <summary>False when the caller passed 0, meaning "leave this limit alone".</summary>
        public bool Requested { get; set; }

        /// <summary>The milliwatt value actually sent, after clamping.</summary>
        public uint RequestedMw { get; set; }

        /// <summary>True when the caller's value was outside the allowed range and was clamped.</summary>
        public bool WasClamped { get; set; }

        /// <summary>
        /// Mailbox status. <see cref="RyzenSmu.SmuStatus.UnknownCmd"/> means this CPU family has
        /// no confirmed message ID for the limit, not that the hardware refused it.
        /// </summary>
        public RyzenSmu.SmuStatus Status { get; set; } = RyzenSmu.SmuStatus.Failed;

        /// <summary>True when the mailbox accepted the message. Not proof the limit is in force.</summary>
        public bool Accepted => !Requested || Status == RyzenSmu.SmuStatus.Ok;

        public override string ToString() =>
            !Requested ? "unchanged"
                       : $"{RequestedMw / 1000.0:0.###} W -> {Status}{(WasClamped ? " (clamped)" : "")}";
    }

    /// <summary>
    /// Outcome of an <see cref="AmdUndervoltProvider.ApplyPowerLimits"/> call.
    ///
    /// "Accepted" throughout means the SMU mailbox took the message, not that the silicon is
    /// running at the new limit. See the remarks on ApplyPowerLimits.
    /// </summary>
    public class AmdPowerLimitReport
    {
        public AmdPowerLimitStep Stapm { get; set; } = new();
        public AmdPowerLimitStep Fast { get; set; } = new();
        public AmdPowerLimitStep Slow { get; set; } = new();
        public AmdPowerLimitStep ApuSlow { get; set; } = new();

        /// <summary>Every requested limit was accepted by its mailbox.</summary>
        public bool AllAccepted => Stapm.Accepted && Fast.Accepted && Slow.Accepted && ApuSlow.Accepted;

        /// <summary>At least one limit was requested and accepted.</summary>
        public bool AnyAccepted =>
            (Stapm.Requested && Stapm.Status == RyzenSmu.SmuStatus.Ok) ||
            (Fast.Requested && Fast.Status == RyzenSmu.SmuStatus.Ok) ||
            (Slow.Requested && Slow.Status == RyzenSmu.SmuStatus.Ok) ||
            (ApuSlow.Requested && ApuSlow.Status == RyzenSmu.SmuStatus.Ok);

        public override string ToString() =>
            $"stapm {Stapm}; fast {Fast}; slow {Slow}; apu-slow {ApuSlow}";
    }
}
