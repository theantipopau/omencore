namespace OmenCore.Models
{
    /// <summary>
    /// AMD Ryzen CPU family identifiers for SMU communication.
    /// Based on G-Helper/UXTU implementation.
    /// </summary>
    public enum RyzenFamily
    {
        Unknown = -999,
        Zen1Plus = -1,      // Zen1/+ Desktop
        Raven = 0,          // Raven Ridge
        Picasso = 1,        // Picasso
        Dali = 2,           // Dali
        RenoirLucienne = 3, // Renoir/Lucienne
        Matisse = 4,        // Matisse (Desktop)
        VanGogh = 5,        // Van Gogh (Steam Deck)
        Vermeer = 6,        // Vermeer (Desktop)
        CezanneBarcelo = 7, // Cezanne/Barcelo
        Rembrandt = 8,      // Rembrandt
        Phoenix = 9,        // Phoenix (Ryzen 7040)
        RaphaelDragonRange = 10, // Raphael/Dragon Range (Desktop)
        Mendocino = 11,     // Mendocino
        HawkPoint = 12,     // Hawk Point (Ryzen 8040)
        StrixPoint = 13,    // Strix Point (Ryzen AI 300)
        StrixHalo = 14,     // Strix Halo (Ryzen AI MAX)
        FireRange = 15      // Fire Range (HX series)
    }

    /// <summary>
    /// AMD Ryzen undervolt settings using Curve Optimizer.
    /// </summary>
    public class RyzenUndervoltOffset
    {
        /// <summary>
        /// All-core Curve Optimizer offset (negative = undervolt).
        /// Typical range: -30 to 0 for most chips.
        /// </summary>
        public int AllCoreCO { get; set; }

        /// <summary>
        /// iGPU Curve Optimizer offset (for APUs with integrated graphics).
        /// </summary>
        public int IgpuCO { get; set; }

        public RyzenUndervoltOffset Clone() => new()
        {
            AllCoreCO = this.AllCoreCO,
            IgpuCO = this.IgpuCO
        };
    }

    /// <summary>
    /// AMD Ryzen power limit settings.
    /// </summary>
    public class RyzenPowerLimits
    {
        /// <summary>
        /// STAPM (Skin Temperature Aware Power Management) limit in mW.
        /// Sustained power limit.
        /// </summary>
        public uint StapmLimit { get; set; }

        /// <summary>
        /// Fast limit (short boost) in mW.
        /// </summary>
        public uint FastLimit { get; set; }

        /// <summary>
        /// Slow limit (long TDP) in mW.
        /// </summary>
        public uint SlowLimit { get; set; }

        /// <summary>
        /// APU slow limit (PPT LIMIT APU) in mW. Distinct from <see cref="SlowLimit"/>: it
        /// governs the combined APU package rather than the CPU PPT domain, and on parts that
        /// expose it, it becomes the binding limit once the other three are raised.
        /// </summary>
        public uint ApuSlowLimit { get; set; }

        /// <summary>
        /// Temperature limit in degrees Celsius.
        /// </summary>
        public uint TctlTemp { get; set; }
    }

    /// <summary>
    /// AMD Ryzen CPU detection result.
    /// </summary>
    public class RyzenCpuInfo
    {
        public string CpuName { get; set; } = string.Empty;
        public string CpuModel { get; set; } = string.Empty;
        public RyzenFamily Family { get; set; } = RyzenFamily.Unknown;
        public bool SupportsUndervolt { get; set; }
        public bool SupportsIgpuUndervolt { get; set; }
    }
}
