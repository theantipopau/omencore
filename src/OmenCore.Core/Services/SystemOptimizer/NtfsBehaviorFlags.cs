namespace OmenCore.Services.SystemOptimizer
{
    /// <summary>
    /// Decodes the DWORD values fsutil writes to NTFS behavior registry keys
    /// (e.g. NtfsDisableLastAccessUpdate). `fsutil behavior set &lt;key&gt; &lt;0-3&gt;` stores the
    /// mode in the low 2 bits and ORs in 0x80000000 to mark the value as explicitly
    /// configured (vs. an absent key, which means the system default applies) - so a
    /// successful "disable" apply leaves the registry at 0x80000001, not 1. Comparing
    /// the raw DWORD directly to a mode value misses this and always reports the
    /// setting as inactive right after a successful apply.
    /// </summary>
    public static class NtfsBehaviorFlags
    {
        private const int ModeMask = 0x3;

        /// <summary>Extracts the 0-3 behavior mode from a raw NTFS behavior DWORD.</summary>
        public static int ExtractMode(int rawValue) => rawValue & ModeMask;

        /// <summary>
        /// True if the raw value's mode means "last access updates are disabled"
        /// (1 = disabled, 2 = system-managed disabled).
        /// </summary>
        public static bool IsLastAccessDisabled(int rawValue)
        {
            var mode = ExtractMode(rawValue);
            return mode == 1 || mode == 2;
        }
    }
}
