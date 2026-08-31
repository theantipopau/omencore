namespace OmenCore.Models
{
    public class FanCurvePoint
    {
        public int TemperatureC { get; set; }
        public int FanPercent { get; set; }
        public double FanSpeedRpm { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
