namespace OmenCore.Corsair
{
    public class CorsairDpiStage
    {
        public string Name { get; set; } = string.Empty;
        public int Dpi { get; set; }
        public bool IsDefault { get; set; }
        public bool AngleSnapping { get; set; }
        public double LiftOffDistanceMm { get; set; }
    }
}
