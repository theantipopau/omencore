using System.Windows.Input;

namespace OmenCore.Corsair
{
    public class MacroAction
    {
        public Key Key { get; set; }
        public int DelayMs { get; set; }
        public bool KeyDown { get; set; }
    }
}
