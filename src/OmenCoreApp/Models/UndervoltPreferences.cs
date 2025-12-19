namespace OmenCore.Models
{
    public class UndervoltPreferences
    {
        public UndervoltOffset DefaultOffset { get; set; } = new() { CoreMv = -75, CacheMv = -50 };
        public bool RespectExternalControllers { get; set; } = true;
        public int ProbeIntervalMs { get; set; } = 4000;

        // Enable per-core undervolting
        public bool EnablePerCoreUndervolt { get; set; } = false;

        // Per-core offsets (indexed by logical core, null means disabled)
        public int?[]? PerCoreOffsetsMv { get; set; }
    }
}
