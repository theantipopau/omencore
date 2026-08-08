using System;
using System.Threading;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services.Diagnostics;

namespace OmenCore.Services
{
    /// <summary>
    /// Lifts the APU half of an under-rated adapter's power clamp, by writing the four AMD SMU
    /// power limits directly and holding them there.
    ///
    /// THE CLAMP. On an under-rated supply this platform clamps the APU as well as the GPU, and the
    /// two are separate mechanisms with separate fixes. Measured on board 8D87 with a 280 W supply
    /// against a 330 W requirement, the SMU limits read:
    ///
    ///     STAPM LIMIT      25.000 W     <- clamped
    ///     PPT LIMIT FAST   25.000 W     <- clamped
    ///     PPT LIMIT SLOW   25.000 W     <- clamped
    ///     PPT LIMIT APU    45.000 W     <- untouched
    ///
    /// On the 330 W adapter the same four all read 45 W. So the clamp hits three of the four, and
    /// the fourth is left holding this machine's real number. That asymmetry is also why all four
    /// have to be written together: raising only the first three hands the ceiling to PPT LIMIT APU
    /// and produces a hard wall at ~46 W instead of removing one.
    ///
    /// HOW THE 25 W GETS IN IS UNKNOWN, and it does not matter here. It is excluded from AML, AMD
    /// PMF, NVPCF, Windows PPM, HP's userland and the EC mailbox block that merely mirrors it - but
    /// the SMU limits it produces are ordinary, writable ones, so this goes around the mechanism
    /// rather than through it. Nothing in this file needs an EC write.
    ///
    /// WHAT IT IS WORTH. 25.02 W pinned, to ~51 W sustained with 60.7 W peaks, on the 280 W adapter.
    ///
    /// TWO THINGS THIS CANNOT DO, both of which the callers have to state rather than paper over:
    ///
    ///   1. It cannot verify. The SMU returns Ok for messages that change nothing, and the PawnIO
    ///      transport has no readback. Acceptance here means the mailbox took the message, and the
    ///      only confirmation is package power under load - see tools/SmuProbe --limits, which
    ///      reads the PM table over an independent transport.
    ///   2. It cannot set and forget. The ACPI power path pushes its own numbers into these same
    ///      registers: after a run that left all four at 45 W they later read 71/71/60/45, where 60
    ///      is exactly this board's NVPCF ATPP. A written limit holds for the whole of a sustained
    ///      load and then drifts back once the load ends. That is what <see cref="Engage"/>'s
    ///      re-assert loop is for, and why this is a held state rather than a one-shot button.
    ///
    /// Nothing here survives a reboot. Every limit is runtime SMU state.
    /// </summary>
    public sealed class ApuPowerClampService : IDisposable
    {
        /// <summary>
        /// How often the limits are written again. 30 s matches the interval the notebook's own
        /// -Watch mode uses, and is well inside the window a written limit was observed to hold.
        /// </summary>
        private static readonly TimeSpan ReassertInterval = TimeSpan.FromSeconds(30);

        private const string TimerName = "ApuPowerClampHold";

        /// <summary>
        /// Minimum fraction of the machine's required adapter rating that a supply must offer
        /// before this is offered on it.
        ///
        /// This is the responsible version of "size it to the supply". The clamp is a policy number
        /// rather than an electrical protection - a 280 W supply carried the whole system with the
        /// battery at exactly 0 W throughout, and the clamp is not even proportional - but that
        /// argument runs out somewhere, and 60% is where the measurements stop. The 200 W supply
        /// (61% of this machine's 330 W) is the smallest one this has been measured on and it was
        /// not remotely stressed; a 100 W USB-C dock (30%) is a supply where the clamp is defensible
        /// on the numbers, and the notebook's playbook says in terms not to run this there.
        /// </summary>
        private const double MinimumSupplyFraction = 0.60;

        private readonly LoggingService _logging;
        private readonly AmdUndervoltProvider _undervolt;
        private readonly object _sync = new();

        private Timer? _holdTimer;
        private uint _heldWatts;
        private bool _disposed;

        public ApuPowerClampService(LoggingService logging, AmdUndervoltProvider undervolt)
        {
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _undervolt = undervolt ?? throw new ArgumentNullException(nameof(undervolt));
        }

        /// <summary>True while the re-assert loop is running.</summary>
        public bool IsHeld
        {
            get { lock (_sync) { return _holdTimer != null; } }
        }

        /// <summary>The limit currently being held, in watts, or 0 when nothing is held.</summary>
        public uint HeldWatts
        {
            get { lock (_sync) { return _heldWatts; } }
        }

        /// <summary>
        /// Whether this machine's firmware states a CPU-with-GPU power budget, and what it is.
        ///
        /// This is the target, and it is read from the machine rather than chosen. HP's own
        /// <c>Default 0x28</c> capability block carries the CPU power limit this SKU is designed to
        /// run at while the GPU is also loaded - 60 W on board 8D87, which is exactly the combined
        /// case this feature creates. Asking for the firmware's own number for the situation keeps
        /// the rule the whole investigation ran on: only ever write values this firmware writes
        /// itself. A machine that does not answer 0x28 gets no target and no offer, rather than a
        /// constant borrowed from a board it was not measured on.
        /// </summary>
        public static uint TargetWattsFor(HpWmiBios.SystemDesignData? design) =>
            design is HpWmiBios.SystemDesignData data && data.DefaultCpuPowerLimitWithGpuWatts > 0
                ? (uint)data.DefaultCpuPowerLimitWithGpuWatts
                : 0;

        /// <summary>
        /// Whether the supply is one this should be offered on at all.
        ///
        /// Two separate refusals, and they are not the same refusal. A USB-C PD source is excluded
        /// outright: the dock negotiates 100 W, the platform budget drops with it, and the dGPU
        /// alone after a restart is already within ~20 W of the whole source before any APU is
        /// counted. A barrel adapter is judged on its rating against what the machine asks for.
        /// </summary>
        public static bool SupplyCanCarryIt(HpWmiBios.AdapterInfo adapter, int requiredWatts, out string reason)
        {
            if (adapter.Status == HpWmiBios.SmartAdapterStatus.ConnectedTypeC)
            {
                reason = "This is a USB-C Power Delivery supply. Raising the CPU power limit on top " +
                         "of the GPU restart is not offered there: the source negotiates far less " +
                         "than this chassis expects, and the GPU alone already accounts for most of it.";
                return false;
            }

            if (!adapter.PowerRatingKnown || adapter.PowerRatingWatts <= 0 || requiredWatts <= 0)
            {
                reason = "The connected adapter's rating was not reported, so there is no way to " +
                         "judge whether it can carry a raised CPU limit.";
                return false;
            }

            if (adapter.PowerRatingWatts < requiredWatts * MinimumSupplyFraction)
            {
                reason = $"The connected {adapter.PowerRatingWatts} W adapter is too far below this " +
                         $"machine's {requiredWatts} W requirement to raise the CPU limit on top of " +
                         "the GPU restart.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Whether the CPU is one whose SMU message IDs are known rather than guessed. The IDs come
        /// from RyzenAdj's lib/api.c verbatim; a family that is not listed there gets no offer,
        /// because a wrong message ID returns Ok and does nothing.
        /// </summary>
        public static bool CpuIsSupported(RyzenFamily family) =>
            AmdUndervoltProvider.FamilySupportsPptLimits(family)
            && AmdUndervoltProvider.FamilySupportsApuSlowLimit(family);

        /// <summary>The outcome of one attempt, in the terms the panel reports it.</summary>
        public sealed record ClampLiftResult(bool Accepted, string Message);

        /// <summary>
        /// Write all four limits and start holding them.
        ///
        /// Returns what the mailboxes said. It does NOT return whether the silicon is running at
        /// the new limit, because nothing on this path can ask.
        /// </summary>
        public ClampLiftResult Engage(uint targetWatts)
        {
            if (targetWatts == 0)
            {
                return new ClampLiftResult(false,
                    "This machine's firmware did not report a CPU power budget, so there is no " +
                    "target to ask for.");
            }

            AmdPowerLimitReport report;

            try
            {
                report = WriteAll(targetWatts);
            }
            catch (Exception ex)
            {
                _logging.Error("APU clamp lift failed", ex);
                return new ClampLiftResult(false, $"The CPU power limits could not be written: {ex.Message}");
            }

            if (!report.AnyAccepted)
            {
                return new ClampLiftResult(false,
                    "The SMU refused every CPU power limit, so nothing was changed on the CPU side. " +
                    $"({report})");
            }

            StartHold(targetWatts);

            // Deliberately does not say "the CPU is now running at N W". The mailbox took the
            // message; whether the silicon honoured it is a different question and this cannot ask
            // it. Saying which one this is, is the whole point.
            var partial = report.AllAccepted
                ? string.Empty
                : $" Not all four were accepted ({report}), so the ceiling may have passed to " +
                  "whichever one refused.";

            return new ClampLiftResult(true,
                $"The four CPU power limits were set to {targetWatts} W and OmenCore is now " +
                $"re-asserting them every {ReassertInterval.TotalSeconds:0} seconds - the platform " +
                "otherwise pushes its own numbers back into them once a load ends." + partial +
                " OmenCore cannot read these back, so this is what the SMU accepted rather than what " +
                "it is running: confirm it under load, where the clamp shows as CPU package power " +
                "pinned around 25 W.");
        }

        /// <summary>
        /// Stop holding the limits.
        ///
        /// Does not put the clamp back. Writing 25 W into these registers would be imposing a clamp
        /// rather than releasing one, and this has no business doing that on the way out - the
        /// platform re-asserts its own numbers within a load or two, and a reboot restores stock.
        /// </summary>
        public void Release()
        {
            lock (_sync)
            {
                if (_holdTimer == null) return;

                _holdTimer.Dispose();
                _holdTimer = null;
                _heldWatts = 0;
            }

            BackgroundTimerRegistry.Unregister(TimerName);
            _logging.Info("APU clamp lift released - the CPU power limits are no longer re-asserted. " +
                          "The platform will take them back on its own; a reboot restores stock.");
        }

        private void StartHold(uint targetWatts)
        {
            lock (_sync)
            {
                _holdTimer?.Dispose();
                _heldWatts = targetWatts;
                _holdTimer = new Timer(_ => Reassert(), null, ReassertInterval, ReassertInterval);
            }

            BackgroundTimerRegistry.Register(
                TimerName,
                nameof(ApuPowerClampService),
                $"Re-asserts the four AMD SMU power limits at {targetWatts} W. Required because the " +
                "ACPI power path writes its own numbers into the same registers once a load ends.",
                (int)ReassertInterval.TotalMilliseconds,
                BackgroundTimerTier.Optional);
        }

        private void Reassert()
        {
            uint watts;
            lock (_sync)
            {
                if (_holdTimer == null || _disposed) return;
                watts = _heldWatts;
            }

            if (watts == 0) return;

            try
            {
                var report = WriteAll(watts);

                // Logged at debug on the way through: this fires twice a minute for as long as the
                // hold is engaged, and a session-long info-level heartbeat would bury everything
                // else in the log. A refusal is worth saying out loud, once per occurrence.
                if (report.AnyAccepted)
                {
                    _logging.Debug($"APU clamp hold: re-asserted {watts} W ({report})");
                }
                else
                {
                    _logging.Warn($"APU clamp hold: the SMU refused every limit this cycle ({report})");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"APU clamp hold: re-assert failed - {ex.Message}");
            }
        }

        /// <summary>
        /// All four limits, at the same value, in one call.
        ///
        /// Same value for all four on purpose: this is restoring a machine to the budget its own
        /// firmware states, not shaping a boost curve. A fast limit below the slow limit would
        /// truncate boost rather than raise sustained power, and staggering them would be tuning -
        /// which is a different feature with a different justification.
        /// </summary>
        private AmdPowerLimitReport WriteAll(uint watts)
        {
            var mw = watts * 1000;

            return _undervolt.ApplyPowerLimits(new RyzenPowerLimits
            {
                StapmLimit = mw,
                FastLimit = mw,
                SlowLimit = mw,
                ApuSlowLimit = mw
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // An app that exits holding this leaves a registered timer behind and a user with no way
            // to stop it. Releasing costs nothing: the limits are runtime state either way.
            Release();
        }
    }
}
