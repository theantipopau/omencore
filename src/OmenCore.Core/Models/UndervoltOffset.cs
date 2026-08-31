namespace OmenCore.Models
{
    public class UndervoltOffset
    {
        public double CoreMv { get; set; }
        public double CacheMv { get; set; }

        // Per-core voltage offsets (null means use global CoreMv)
        public int?[]? PerCoreOffsetsMv { get; set; }

        public UndervoltOffset Clone() => new()
        {
            CoreMv = this.CoreMv,
            CacheMv = this.CacheMv,
            PerCoreOffsetsMv = this.PerCoreOffsetsMv?.Clone() as int?[]
        };

        public bool HasPerCoreOffsets => PerCoreOffsetsMv != null && PerCoreOffsetsMv.Length > 0;
    }
}
