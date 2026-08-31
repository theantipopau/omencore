using System;
using System.Collections.Generic;
using System.Management;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Board identity, and the one place where a feature is restricted to boards it was measured on.
    ///
    /// OmenCore spans years of OMEN and Victus firmware, and most of what is known about any given
    /// surface was learned on ONE machine. Where a capability can be probed, probe it - the shape
    /// test in <see cref="HidLampArray.OpenLightBar"/> is doing real work and would be worth keeping
    /// with no allowlist at all. This is for the window between "measured here" and "confirmed
    /// there", and it should shrink: an entry that gets broadly confirmed belongs in a real probe.
    /// </summary>
    public static class OmenBoard
    {
        private static string? _product;
        private static bool _read;

        /// <summary>
        /// Win32_BaseBoard.Product - HP's four-character board id, e.g. "8D87". Empty when it
        /// cannot be read, which is treated as "unknown board" rather than as any particular one.
        /// </summary>
        public static string Product
        {
            get
            {
                if (_read) return _product ?? string.Empty;
                _read = true;

                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard");
                    foreach (ManagementObject o in searcher.Get())
                    {
                        _product = o["Product"]?.ToString()?.Trim();
                        break;
                    }
                }
                catch
                {
                    // An unreadable baseboard is an unknown board, and unknown boards get the
                    // conservative path. Nothing here is worth failing startup over.
                    _product = null;
                }

                return _product ?? string.Empty;
            }
        }

        /// <summary>
        /// Boards where the light bar was confirmed reachable over its own HID LampArray, without
        /// administrator.
        ///
        /// 8D87 - OMEN MAX 16-ak0xxx, BIOS F.07 / EC 40.38. The bar enumerates as "HID VHF Driver"
        /// 0461:0001, Kind Scene, four lamps across 323 x 5 mm; colour writes were confirmed by eye
        /// from a non-elevated process. Whether that virtual device exists on any other OMEN, and is
        /// the light bar there, one machine cannot say - hence a list rather than a blanket enable.
        /// </summary>
        private static readonly HashSet<string> HidLightBarBoards =
            new(StringComparer.OrdinalIgnoreCase) { "8D87" };

        /// <summary>
        /// Whether to reach the light bar over HID on this machine.
        ///
        /// The HID path is used ONLY where HP's WMI surface is unavailable - unelevated, where the
        /// bar currently does nothing at all. It never displaces WMI, so a false here restores
        /// exactly today's behaviour and an elevated user is unaffected either way.
        /// </summary>
        public static bool SupportsHidLightBar(string? product = null) =>
            HidLightBarBoards.Contains((product ?? Product).Trim());
    }
}
