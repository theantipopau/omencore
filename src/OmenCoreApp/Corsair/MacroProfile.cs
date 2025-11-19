using System.Collections.Generic;

namespace OmenCore.Corsair
{
    public class MacroProfile
    {
        public string Name { get; set; } = string.Empty;
        public List<MacroAction> Actions { get; set; } = new();
    }
}
