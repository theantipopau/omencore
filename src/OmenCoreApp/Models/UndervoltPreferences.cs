namespace OmenCore.Models
{
    public class UndervoltPreferences
    {
        public UndervoltOffset DefaultOffset { get; set; } = new() { CoreMv = -75, CacheMv = -50 };
        public bool RespectExternalControllers { get; set; } = true;
        public int ProbeIntervalMs { get; set; } = 4000;
    }
}
