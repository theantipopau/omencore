using System;
using Microsoft.Win32;

namespace OmenCore.Services.KeyboardLighting
{
    /// <summary>
    /// Whether Windows Dynamic Lighting is going to repaint a device we just painted.
    ///
    /// WHY THIS EXISTS. Dynamic Lighting is a second owner of every HID LampArray on the machine,
    /// and it is the one that wins whenever no application holds the device. An application that
    /// takes host control locks Windows out only for as long as it lives; on the way down it hands
    /// the lamps back and Windows repaints within one refresh interval - 33 ms on 8D87. The visible
    /// result is a picture that holds perfectly while the app is running and reverts the instant it
    /// closes, which reads as "this app cannot save my colours" and is nothing of the sort.
    ///
    /// There is no API that reports this. <c>LampArray.AvailabilityChanged</c> covers an app that
    /// has registered for the ambient contract, and that contract requires MSIX package identity -
    /// the AmbientLightingServer only accepts clients with it. So for an unpackaged app the user's
    /// settings are readable in exactly one place, which is the registry, and reading it is the
    /// difference between telling the user what is happening and letting them conclude the feature
    /// is broken.
    ///
    /// READ-ONLY, DELIBERATELY. These are another feature's settings and the user owns them. This
    /// type never writes: it reports, and the UI links to the Settings page where the toggle lives.
    /// </summary>
    public sealed class DynamicLightingState
    {
        private const string RootKey = @"Software\Microsoft\Lighting";
        private const string DevicesKey = RootKey + @"\Devices";

        /// <summary>The master "Use Dynamic Lighting on my devices" toggle.</summary>
        public bool GlobalEnabled { get; init; }

        /// <summary>Whether a device card for this VID/PID was found under the Devices key.</summary>
        public bool DeviceFound { get; init; }

        /// <summary>This device's own card toggle. True when no card exists, since absence is not opt-out.</summary>
        public bool DeviceEnabled { get; init; } = true;

        /// <summary>
        /// "Compatible apps in the foreground always control lighting", per device.
        ///
        /// Recorded rather than acted on. It governs which of two RUNNING applications gets the
        /// device, so it has no bearing on what happens after this one exits - the case that
        /// prompted all of this. It is worth surfacing when someone reports the opposite complaint,
        /// that OmenCore is being overrun while it is open.
        /// </summary>
        public bool ControlledByForegroundApp { get; init; }

        /// <summary>
        /// Whether Windows will take this device back when OmenCore releases it.
        ///
        /// The conjunction is the honest reading of two independent toggles: the master switch and
        /// the per-device card. What has been MEASURED on 8D87 is the master - off, a picture holds
        /// across an exit; on, it does not. The device card was read in the off state only, so it
        /// is treated as a veto rather than assumed to track the master.
        /// </summary>
        public bool WillRepaintWhenReleased => GlobalEnabled && DeviceEnabled;

        /// <summary>
        /// Read the settings for one device, by USB VID/PID.
        ///
        /// Matched on the <c>VID_xxxx&amp;PID_xxxx</c> fragment rather than the full instance path,
        /// because the rest of that path carries the USB port. Microsoft's own documentation warns
        /// that "ambient background settings are tied to a device and the port it's connected on",
        /// so an exact-path match would silently stop matching if the device moved - which for an
        /// internal keyboard means after a firmware update or a chassis service, exactly when
        /// somebody is already looking for something to blame.
        ///
        /// Never throws. A missing key, a denied read or a value of the wrong type all mean the same
        /// thing to a caller: nothing to warn about. This is a diagnostic, and a diagnostic that can
        /// take the lighting page down with it is worse than no diagnostic.
        /// </summary>
        public static DynamicLightingState Read(ushort vendorId, ushort productId)
        {
            try
            {
                bool global = ReadFlag(RootKey, "AmbientLightingEnabled") ?? false;

                using var devices = Registry.CurrentUser.OpenSubKey(DevicesKey);
                if (devices == null)
                    return new DynamicLightingState { GlobalEnabled = global, DeviceFound = false };

                string fragment = $"VID_{vendorId:X4}&PID_{productId:X4}";

                foreach (string name in devices.GetSubKeyNames())
                {
                    if (name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    using var device = devices.OpenSubKey(name);
                    if (device == null) continue;

                    return new DynamicLightingState
                    {
                        GlobalEnabled = global,
                        DeviceFound = true,
                        // A card that exists but has never been touched has no value written. The
                        // device is being driven in that state, so absent reads as enabled.
                        DeviceEnabled = ReadFlag(device, "AmbientLightingEnabled") ?? true,
                        ControlledByForegroundApp = ReadFlag(device, "ControlledByForegroundApp") ?? false
                    };
                }

                return new DynamicLightingState { GlobalEnabled = global, DeviceFound = false };
            }
            catch (Exception)
            {
                // Deliberately silent. The only consequence is that no warning is shown.
                return new DynamicLightingState { GlobalEnabled = false, DeviceFound = false };
            }
        }

        private static bool? ReadFlag(string subKey, string valueName)
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            return key == null ? null : ReadFlag(key, valueName);
        }

        private static bool? ReadFlag(RegistryKey key, string valueName) =>
            key.GetValue(valueName) is int value ? value != 0 : null;
    }
}
