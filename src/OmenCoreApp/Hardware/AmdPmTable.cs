using System.Collections.Generic;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Which float index of the SMU power-metrics table holds which power limit.
    ///
    /// This is per-table-version data. It is NOT ported from another codename's offset table:
    /// every entry below was identified on hardware by writing one limit at a time and recording
    /// which index followed, with an independent implementation reading the same registers as the
    /// anchor. <c>tools/SmuProbe --pmtable</c> is that harness, and it is how a new version gets
    /// added. A guessed offset produces a plausible number from the wrong slot, which is worse
    /// than no reading at all - so an unknown version reads back nothing rather than something.
    /// </summary>
    public sealed record AmdPmTableLayout(int Stapm, int Fast, int Slow, int ApuSlow)
    {
        /// <summary>Highest index this layout refers to, so a read can be sized to it.</summary>
        public int MaxIndex
        {
            get
            {
                int max = Stapm;
                if (Fast > max) max = Fast;
                if (Slow > max) max = Slow;
                if (ApuSlow > max) max = ApuSlow;
                return max;
            }
        }
    }

    /// <summary>
    /// The four power limits as the SMU itself reports them, in watts.
    ///
    /// This is a READ of live SMU state, not a record of what was requested. That distinction is
    /// the point of the type: OmenCore could always say what it asked for and never what the
    /// silicon was running.
    /// </summary>
    public sealed record AmdPowerLimitReadback(
        uint TableVersion,
        double StapmWatts,
        double FastWatts,
        double SlowWatts,
        double ApuSlowWatts);

    public static class AmdPmTable
    {
        /// <summary>
        /// Table versions whose layout has been measured on hardware.
        ///
        /// 0x5D000B - Strix Point (AMD Ryzen AI 9 HX 375), identified 2026-08-22 on board 8D87.
        /// Indices 4 (slow) and 6 (apu-slow) were each confirmed directly: writing that limit
        /// alone moved that index alone. Indices 0 and 2 both follow the FAST write on this part
        /// because STAPM mirrors the fast limit here - the stapm-limit message is accepted and
        /// inert - so they cannot be separated by writing. Their assignment follows the mapping
        /// confirmed at 4 and 6: the limits appear in table order at a stride of 2, and 0 and 2
        /// are the first two positions of that same sequence.
        /// </summary>
        private static readonly Dictionary<uint, AmdPmTableLayout> Layouts = new()
        {
            [0x5D000Bu] = new AmdPmTableLayout(Stapm: 0, Fast: 2, Slow: 4, ApuSlow: 6)
        };

        public static bool TryGetLayout(uint tableVersion, out AmdPmTableLayout? layout) =>
            Layouts.TryGetValue(tableVersion, out layout);

        /// <summary>
        /// Bytes to read for a layout. Only the head of the table is needed, and reading less of
        /// it is less time holding the PCI bus mutex. Rounded up to a multiple of 8 because the
        /// module hands back 64-bit words.
        /// </summary>
        public static uint ReadSizeBytes(AmdPmTableLayout layout)
        {
            uint bytes = (uint)(layout.MaxIndex + 1) * 4;
            return (bytes + 7) / 8 * 8;
        }
    }
}
