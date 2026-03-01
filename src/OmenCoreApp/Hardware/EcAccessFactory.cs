using System;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Factory for creating EC access providers with automatic fallback.
    /// Tries PawnIO first (Secure Boot compatible). WinRing0 fallback is disabled by default
    /// to avoid Defender/anti-cheat false positives and can be opt-in via environment variable.
    /// </summary>
    public static class EcAccessFactory
    {
        public enum EcBackend
        {
            None,
            PawnIO,
            WinRing0
        }

        private static IEcAccess? _instance;
        private static EcBackend _activeBackend = EcBackend.None;
        private static readonly object _lock = new();

        /// <summary>
        /// Gets the currently active EC backend type.
        /// </summary>
        public static EcBackend ActiveBackend => _activeBackend;

        /// <summary>
        /// Gets or creates an EC access provider, trying backends in order of preference.
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

                // Clean up any previous failed instance
                _instance?.Dispose();
                _instance = null;
                _activeBackend = EcBackend.None;

                // Try PawnIO first (Secure Boot compatible, signed driver)
                if (TryInitializePawnIO())
                {
                    return _instance;
                }

                // Optional legacy fallback: WinRing0 (requires Secure Boot disabled)
                // Disabled by default to avoid Defender false positives.
                if (IsWinRing0FallbackEnabled() && TryInitializeWinRing0())
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
                // If we already have the preferred backend, return it
                if (_instance != null && _instance.IsAvailable && _activeBackend == preferredBackend)
                {
                    return _instance;
                }

                // Clean up existing instance
                _instance?.Dispose();
                _instance = null;
                _activeBackend = EcBackend.None;

                bool success = preferredBackend switch
                {
                    EcBackend.PawnIO => TryInitializePawnIO(),
                    EcBackend.WinRing0 => TryInitializeWinRing0(),
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

        private static bool TryInitializeWinRing0()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[EcAccessFactory] Trying WinRing0 backend...");
                var winRing0 = new WinRing0EcAccess();

                // Try common WinRing0 device paths
                string[] devicePaths = new[]
                {
                    @"\\.\WinRing0_1_2_0",
                    @"\\.\WinRing0",
                    @"\\.\OmenMon" // OmenMon's driver
                };

                foreach (var path in devicePaths)
                {
                    if (winRing0.Initialize(path))
                    {
                        _instance = winRing0;
                        _activeBackend = EcBackend.WinRing0;
                        System.Diagnostics.Debug.WriteLine($"[EcAccessFactory] WinRing0 backend initialized via {path}");
                        return true;
                    }
                }

                winRing0.Dispose();
                System.Diagnostics.Debug.WriteLine("[EcAccessFactory] WinRing0 backend not available (likely blocked by Secure Boot)");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EcAccessFactory] WinRing0 initialization failed: {ex.Message}");
                return false;
            }
        }

        private static bool IsWinRing0FallbackEnabled()
        {
            var env = Environment.GetEnvironmentVariable("OMENCORE_ENABLE_WINRING0");
            return string.Equals(env, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets a human-readable status message about EC access.
        /// </summary>
        public static string GetStatusMessage()
        {
            return _activeBackend switch
            {
                EcBackend.PawnIO => "EC access via PawnIO (Secure Boot compatible)",
                EcBackend.WinRing0 => "EC access via WinRing0 (Secure Boot may need to be disabled)",
                _ => "No EC access available - install PawnIO from pawnio.eu (WinRing0 fallback is opt-in via OMENCORE_ENABLE_WINRING0=1)"
            };
        }

        /// <summary>
        /// Checks if any EC backend is available without fully initializing.
        /// </summary>
        public static bool IsAnyBackendAvailable()
        {
            // Quick check for PawnIO installation
            bool pawnIOExists = System.IO.File.Exists(
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "PawnIO", "PawnIOLib.dll"));

            if (pawnIOExists) return true;

            // Check registry for PawnIO
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                if (key != null) return true;
            }
            catch { }

            // We can't quickly check WinRing0 without trying to load it
            // Return true optimistically - full check happens in GetEcAccess()
            return true;
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
