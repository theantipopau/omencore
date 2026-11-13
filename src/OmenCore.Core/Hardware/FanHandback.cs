using System;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    /// <summary>
    /// The minimal, dependency-free fan hand-back: clear the firmware Max flag, restore the Default
    /// thermal mode, and on V0/V1 firmware write the same low level hint <c>WmiFanController</c>'s
    /// EC reset uses (see <see cref="HpWmiBios.ReleaseMaxAndHandBackToBios"/>).
    ///
    /// Exists for the one path that never reaches <c>FanService.Dispose()</c>: uninstall and
    /// upgrade. The installer has to stop a still-running OmenCore because it holds the exe open,
    /// so OnExit - and with it the EC reset inside the dispose chain - does not run. Firmware Max
    /// is a register, not process state: it survives that kill and keeps the fans pinned after
    /// the app that set it is gone, which is the failure mode the installer used to leave behind.
    ///
    /// Deliberately does not build <c>HardwareBringup</c>: capability probing blocks for seconds
    /// and spawns a HardwareWorker, neither of which belongs in an uninstaller.
    /// <c>HpWmiBios.Initialize</c> is a single CIM connect and is the same object every WMI fan
    /// write goes through, so the hand-back here is the same command the controller sends.
    /// </summary>
    public static class FanHandback
    {
        /// <summary>
        /// Hands fan control back to the BIOS. Returns true if the Max flag was cleared or the
        /// Default thermal mode was accepted; false when WMI BIOS is unavailable or both writes
        /// were refused (unsupported board, firmware rejecting the command).
        /// </summary>
        public static bool RestoreToBiosAuto(LoggingService? logging = null)
        {
            try
            {
                using var wmi = new HpWmiBios(logging);
                if (!wmi.IsAvailable)
                {
                    logging?.Warn("Fan hand-back skipped: WMI BIOS is not available on this machine.");
                    return false;
                }

                var restored = wmi.ReleaseMaxAndHandBackToBios();
                if (restored)
                {
                    logging?.Info("Fan hand-back: firmware Max cleared and Default thermal mode restored.");
                }
                else
                {
                    logging?.Warn("Fan hand-back: the firmware refused both the Max clear and the Default mode write.");
                }

                return restored;
            }
            catch (Exception ex)
            {
                logging?.Warn($"Fan hand-back failed: {ex.Message}");
                return false;
            }
        }
    }
}
