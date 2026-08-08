using OmenCore.Hardware;

namespace OmenCore.Models
{
    /// <summary>
    /// Whether OmenCore lifts an under-rated adapter's power clamps without being asked each time,
    /// and on which supplies.
    ///
    /// Both halves default to off. This writes SMU power limits and restarts a display device -
    /// neither is something an app should start doing because it was installed.
    /// </summary>
    public class AdapterClampLiftSettings
    {
        /// <summary>
        /// Write and hold the four AMD SMU power limits whenever the supply is clamped.
        ///
        /// Safe to do unprompted in a way the GPU half is not: it is an SMU message, nothing is
        /// taken away from anything that is using it, and there is no visible interruption.
        /// </summary>
        public bool LiftCpuLimits { get; set; }

        /// <summary>
        /// Restart the dGPU to drop its clamp - but only while the device is parked in D3.
        ///
        /// The restriction is the whole reason this can be automatic at all. A restart destroys
        /// every graphics and compute context on the device and drops its display outputs; doing
        /// that to a GPU somebody is using is not something to decide on their behalf. A parked
        /// GPU is one the driver has already powered down because nothing is rendering, and in
        /// Hybrid mode the internal panel is on the iGPU, so the restart costs nothing visible.
        ///
        /// It is not a guarantee that no process holds a device handle - D3 says the driver parked
        /// it, not that nobody has it open - which is why the restart still runs the full NVML
        /// quiesce, and re-checks the power state immediately before pulling the device.
        /// </summary>
        public bool LiftGpuWhenParked { get; set; }

        /// <summary>
        /// Only act when the connected adapter is below this many watts. Zero means "whenever the
        /// firmware calls the supply under-rated", which is the verdict the whole panel is built
        /// on and the right default.
        ///
        /// Here for the case where someone has one supply they want this on and another they do
        /// not, and both are under-rated.
        /// </summary>
        public int OnlyBelowAdapterWatts { get; set; }
    }

    /// <summary>What the automatic path should do about one half of the clamp.</summary>
    public enum ClampLiftDecision
    {
        /// <summary>The user has not turned this on.</summary>
        Disabled,

        /// <summary>Nothing to lift - the supply is not one this applies to.</summary>
        NotApplicable,

        /// <summary>The GPU is awake. Not something to take away from whoever is using it.</summary>
        DeferredGpuAwake,

        /// <summary>The panel is on the dGPU, so restarting it would drop the display.</summary>
        DeferredDiscreteDisplay,

        /// <summary>Go ahead.</summary>
        Apply
    }

    /// <summary>
    /// The decisions the automatic path makes, kept apart from the code that acts on them so they
    /// can be tested without an SMU or a GPU to restart.
    /// </summary>
    public static class AdapterClampLiftPolicy
    {
        /// <summary>
        /// Whether this supply is one the settings say to act on: clamped, and under the ceiling if
        /// one was set.
        /// </summary>
        public static bool SupplyQualifies(AdapterClampLiftSettings settings, HpWmiBios.AdapterInfo adapter)
        {
            if (!adapter.HasVerdict || !adapter.IsLowWattage) return false;

            if (settings.OnlyBelowAdapterWatts <= 0) return true;

            // An unreported wattage cannot be shown to be under the ceiling, and a user who set one
            // asked for a narrower rule rather than a wider one.
            return adapter.PowerRatingKnown
                && adapter.PowerRatingWatts > 0
                && adapter.PowerRatingWatts < settings.OnlyBelowAdapterWatts;
        }

        /// <summary>
        /// The CPU half. No state of the machine makes this unsafe to do unprompted - it takes
        /// nothing away from anything - so this is the setting and the supply, and nothing else.
        /// </summary>
        public static ClampLiftDecision DecideCpu(
            AdapterClampLiftSettings settings, HpWmiBios.AdapterInfo adapter, bool cpuHalfIsOffered)
        {
            if (!settings.LiftCpuLimits) return ClampLiftDecision.Disabled;
            if (!cpuHalfIsOffered || !SupplyQualifies(settings, adapter)) return ClampLiftDecision.NotApplicable;

            return ClampLiftDecision.Apply;
        }

        /// <summary>
        /// The GPU half, which turns on two conditions rather than one.
        ///
        /// <paramref name="gpuIsParked"/> should come from the PnP manager's own bookkeeping rather
        /// than from asking the GPU, which would wake the device to answer a question about whether
        /// it is asleep. Unknown counts as awake: this refuses rather than guesses, because the cost
        /// of being wrong is somebody's render.
        ///
        /// <paramref name="displayMode"/> has to be Hybrid or Optimus, where the internal panel is
        /// driven by the iGPU and the restart costs nothing visible. In Discrete mode the panel is
        /// on the dGPU and pulling it takes the display with it. A parked dGPU should not be driving
        /// a panel in the first place, so this is a second lock on a door that ought to be shut - but
        /// the automatic path is exactly where a should-not-happen is worth checking, because there
        /// is nobody watching when it runs. A mode that could not be read is treated as Discrete.
        /// </summary>
        public static ClampLiftDecision DecideGpu(
            AdapterClampLiftSettings settings, HpWmiBios.AdapterInfo adapter,
            bool gpuHalfIsOffered, bool gpuIsParked, HpWmiBios.GpuMode? displayMode)
        {
            if (!settings.LiftGpuWhenParked) return ClampLiftDecision.Disabled;
            if (!gpuHalfIsOffered || !SupplyQualifies(settings, adapter)) return ClampLiftDecision.NotApplicable;

            if (displayMode is not (HpWmiBios.GpuMode.Hybrid or HpWmiBios.GpuMode.Optimus))
            {
                return ClampLiftDecision.DeferredDiscreteDisplay;
            }

            return gpuIsParked ? ClampLiftDecision.Apply : ClampLiftDecision.DeferredGpuAwake;
        }
    }
}
