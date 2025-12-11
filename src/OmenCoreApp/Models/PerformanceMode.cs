namespace OmenCore.Models
{
    public class PerformanceMode
    {
        public string Name { get; set; } = string.Empty;
        public int CpuPowerLimitWatts { get; set; }
        public int GpuPowerLimitWatts { get; set; }
        public string LinkedPowerPlanGuid { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        
        public override string ToString() => Name;
    }
}
