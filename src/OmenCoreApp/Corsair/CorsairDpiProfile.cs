using System.Collections.Generic;

namespace OmenCore.Corsair
{
    public class CorsairDpiProfile
    {
        public string Name { get; set; } = string.Empty;
        public List<CorsairDpiStage> Stages { get; set; } = new List<CorsairDpiStage>();
    }
}