using System;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Factory for creating EC access providers.
    /// Uses PawnIO only; the legacy EC backend was removed to avoid Defender/anti-cheat alerts.
    /// </summary>
    public static class EcAccessFactory
    {
        public enum EcBackend
        {
            None,
            PawnIO
        }

        private static IEcAccess? _instance;
        private static EcBackend _activeBackend = EcBackend.None;
        private static readonly object _lock = new();

        /// <summary>
        /// Gets the currently active EC backend type.
        /// </summary>
        public static EcBackend ActiveBackend => _activeBackend;

        /// <summary>
        /// Gets or creates an EC access provider.
        /// </summary>
        /// <returns>An initialized EC access provider, or null if none available.</returns>
        public static IEcAccess? GetEcAccess()
        {
            lock (_lock)
            {
                if (_instance != null && _instance.IsAvailable)
                {
                    return _instance;
                }

                _instance?.Dispose();
                _instance = null;
                _activeBackend = EcBackend.None;

                if (TryInitializePawnIO())
                {
                    return _instance;
                }

                System.Diagnostics.Debug.WriteLine("[EcAccessFactory] No EC access backend available");
                return null;
            }
        }

        /// <summary>
        /// Forces a specific backend. Useful for testing or user preference.
        /// </summary>
        public static IEcAccess? GetEcAccess(EcBackend preferredBackend)
        {
            lock (_lock)
            {
                if (_instance != null && _instance.IsAvailable && _activeBackend == preferredBackend)
                {
                    return _instance;
                }

                _instance?.Dispose();
                _instance = null;
                _activeBackend = EcBackend.None;

                bool success = preferredBackend switch
                {
                    EcBackend.PawnIO => TryInitializePawnIO(),
                    _ => false
                };

                return success ? _instance : null;
            }
        }

        private static bool TryInitializePawnIO()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[EcAccessFactory] Trying PawnIO backend...");
                var pawnIO = new PawnIOEcAccess();

                if (pawnIO.Initialize(""))
                {
                    _instance = pawnIO;
                    _activeBackend = EcBackend.PawnIO;
                    System.Diagnostics.Debug.WriteLine("[EcAccessFactory] PawnIO backend initialized successfully (Secure Boot compatible)");
                    return true;
                }

                pawnIO.Dispose();
                System.Diagnostics.Debug.WriteLine("[EcAccessFactory] PawnIO backend not available");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EcAccessFactory] PawnIO initialization failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets a human-readable status message about EC access.
        /// </summary>
        public static string GetStatusMessage()
        {
            return _activeBackend switch
            {
                EcBackend.PawnIO => "EC access via PawnIO (Secure Boot compatible)",
                _ => IsPawnIOInstalled()
                    ? "PawnIO installed but EC initialization failed - driver may need a reboot to activate"
                    : "No EC access available - install PawnIO from pawnio.eu"
            };
        }

        /// <summary>
        /// Checks if PawnIO is installed on this system (registry presence only, no driver probe).
        /// </summary>
        private static bool IsPawnIOInstalled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                if (key != null) return true;
                string defaultDll = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "PawnIO", "PawnIOLib.dll");
                return System.IO.File.Exists(defaultDll);
            }
            catch { return false; }
        }

        /// <summary>
        /// Checks if any EC backend is available without fully initializing.
        /// </summary>
        public static bool IsAnyBackendAvailable()
        {
            if (System.IO.File.Exists(
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "PawnIO", "PawnIOLib.dll")))
            {
                return true;
            }

            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Disposes of the current EC access instance.
        /// </summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                _instance?.Dispose();
                _instance = null;
                _activeBackend = EcBackend.None;
            }
        }
    }
}
