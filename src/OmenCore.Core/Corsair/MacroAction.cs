namespace OmenCore.Corsair
{
    // Key is a raw virtual-key code, not a System.Windows.Input.Key - kept UI-framework-neutral
    // for the OmenCore.Core split (docs/ROADMAP_v4.2.1.md, "v4.3.0 candidate slate" item 1).
    // Nothing populates this today: MacroProfiles in the UI are hardcoded placeholder names
    // with empty Actions lists (LightingViewModel.cs), and MacroService.PushEvent - the only
    // code that would ever construct one with a real key - has zero callers anywhere.
    public class MacroAction
    {
        public int Key { get; set; }
        public int DelayMs { get; set; }
        public bool KeyDown { get; set; }
    }
}
