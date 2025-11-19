namespace OmenCore.Models
{
    public class UndervoltOffset
    {
        public double CoreMv { get; set; }
        public double CacheMv { get; set; }

        public UndervoltOffset Clone() => new()
        {
            CoreMv = this.CoreMv,
            CacheMv = this.CacheMv
        };
    }
}
