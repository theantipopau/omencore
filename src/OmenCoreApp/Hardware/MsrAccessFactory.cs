using System;
using Microsoft.Win32;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Factory for creating MSR access providers.
    /// Prefers PawnIO (Secure Boot compatible) over WinRing0 (legacy).
    /// </summary>
    public static class MsrAccessFactory
    {
        /// <summary>
        /// Active backend being used.
        /// </summary>
        public static MsrBackend ActiveBackend { get; private set; } = MsrBackend.None;
        
        /// <summary>
        /// Status message describing the current MSR access state.
        /// </summary>
        public static string StatusMessage { get; private set; } = "Not initialized";
        
        /// <summary>
        /// Create an MSR access provider. Uses PawnIO only (WinRing0 deprecated and removed).
        /// Returns null if PawnIO is not available.
        /// </summary>
        public static IMsrAccess? Create(LoggingService? logging = null)
        {
            // Use PawnIO only (Secure Boot compatible, no antivirus false positives)
            try
            {
                var pawnIO = new PawnIOMsrAccess();
                if (pawnIO.IsAvailable)
                {
                    ActiveBackend = MsrBackend.PawnIO;
                    StatusMessage = "PawnIO MSR access available (Secure Boot compatible)";
                    logging?.Info($"✓ {StatusMessage}");
                    return pawnIO;
                }
                pawnIO.Dispose();
            }
            catch (Exception ex)
            {
                logging?.Debug($"PawnIO MSR init failed: {ex.Message}");
            }
            
            // WinRing0 fallback removed in v2.7.0 to avoid antivirus false positives
            // Users should install PawnIO for MSR access features
            
            // No backend available — distinguish "not installed" from "installed but broken"
            ActiveBackend = MsrBackend.None;
            if (IsPawnIOInstalled())
            {
                StatusMessage = "PawnIO installed but MSR initialization failed — driver may need a reboot to activate";
                logging?.Warn(StatusMessage);
            }
            else
            {
                StatusMessage = "No MSR access available. Install PawnIO for undervolt/TCC features.";
                logging?.Info(StatusMessage);
            }
            return null;
        }
        
        /// <summary>
        /// Check if any MSR backend is available without creating an instance.
        /// </summary>
        public static bool IsAnyBackendAvailable()
        {
            // Check for PawnIO only (WinRing0 removed in v2.7.0)
            try
            {
                using var pawnIO = new PawnIOMsrAccess();
                if (pawnIO.IsAvailable) return true;
            }
            catch { }
            
            return false;
        }
        
        /// <summary>
        /// Check if PawnIO is installed on this system (registry presence only, no driver probe).
        /// </summary>
        private static bool IsPawnIOInstalled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                if (key != null) return true;
                string defaultDll = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "PawnIO", "PawnIOLib.dll");
                return System.IO.File.Exists(defaultDll);
            }
            catch { return false; }
        }
    }
    
    public enum MsrBackend
    {
        None,
        PawnIO,
        [Obsolete("WinRing0 support removed in v2.7.0")]
        WinRing0
    }
}
