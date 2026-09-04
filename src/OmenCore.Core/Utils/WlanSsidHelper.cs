using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OmenCore.Utils
{
    /// <summary>
    /// Minimal P/Invoke wrapper around the Native Wifi API (wlanapi.dll) - the only reliable way
    /// to read the SSID of the currently-connected wireless network on modern Windows.
    ///
    /// This replaces two broken approaches AutomationService.EvaluateWiFiTrigger used to try:
    /// the "root\WlanApi" WMI namespace (NDIS-based, largely obsolete since Vista/7 and absent
    /// on many modern systems), and a fallback that only checked whether *any* wireless
    /// interface was up without reading its SSID at all - meaning a rule configured for
    /// "when connected to Home-WiFi" could silently fire on any network. wlanapi.dll's
    /// WlanQueryInterface(wlan_intf_opcode_current_connection) is the same API the Windows
    /// network flyout itself is built on and is present on every Windows 10/11 install with a
    /// WLAN AutoConfig service (wlansvc) - the fallback this replaces existed because whoever
    /// wrote the original code didn't reach for it; there's no environment where WMI's NDIS
    /// namespace works but this doesn't.
    /// </summary>
    public static class WlanSsidHelper
    {
        private const int WlanApiVersion = 2; // Windows 7+ client version, safe floor for this project's Win10/11 target
        private const int WlanIntfOpcodeCurrentConnection = 7;
        private const int ErrorSuccess = 0;

        [DllImport("wlanapi.dll")]
        private static extern int WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

        [DllImport("wlanapi.dll")]
        private static extern int WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

        [DllImport("wlanapi.dll")]
        private static extern int WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

        [DllImport("wlanapi.dll")]
        private static extern int WlanQueryInterface(
            IntPtr hClientHandle,
            ref Guid pInterfaceGuid,
            int opCode,
            IntPtr pReserved,
            out uint pdwDataSize,
            out IntPtr ppData,
            IntPtr pWlanOpcodeValueType);

        [DllImport("wlanapi.dll")]
        private static extern void WlanFreeMemory(IntPtr pMemory);

        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_INTERFACE_INFO
        {
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strInterfaceDescription;
            public uint isState;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DOT11_SSID
        {
            public uint uSSIDLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] ucSSID;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_ASSOCIATION_ATTRIBUTES
        {
            public DOT11_SSID dot11Ssid;
            public uint dot11BssType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] dot11Bssid;
            public uint dot11PhyType;
            public uint uDot11PhyIndex;
            public uint wlanSignalQuality;
            public uint ulRxRate;
            public uint ulTxRate;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_SECURITY_ATTRIBUTES
        {
            [MarshalAs(UnmanagedType.Bool)] public bool bSecurityEnabled;
            [MarshalAs(UnmanagedType.Bool)] public bool bOneXEnabled;
            public uint dot11AuthAlgorithm;
            public uint dot11CipherAlgorithm;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WLAN_CONNECTION_ATTRIBUTES
        {
            public uint isState;
            public uint wlanConnectionMode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strProfileName;
            public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
            public WLAN_SECURITY_ATTRIBUTES wlanSecurityAttributes;
        }

        /// <summary>
        /// Returns the SSID of whichever wireless interface is currently connected, or null if
        /// no interface is connected or the WLAN AutoConfig service/API isn't available (fails
        /// closed - never throws, matching this project's other optional-hardware-signal helpers
        /// like NvmlInterop).
        /// </summary>
        public static bool TryGetCurrentConnectedSsid(out string? ssid)
        {
            ssid = null;
            var clientHandle = IntPtr.Zero;
            var interfaceListPtr = IntPtr.Zero;

            try
            {
                if (WlanOpenHandle(WlanApiVersion, IntPtr.Zero, out _, out clientHandle) != ErrorSuccess)
                    return false;

                if (WlanEnumInterfaces(clientHandle, IntPtr.Zero, out interfaceListPtr) != ErrorSuccess)
                    return false;

                var numberOfItems = (uint)Marshal.ReadInt32(interfaceListPtr);
                var infoSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();
                // WLAN_INTERFACE_INFO_LIST is { dwNumberOfItems, dwIndex, WLAN_INTERFACE_INFO[] } - the
                // array starts right after the two leading 4-byte fields.
                var arrayStart = IntPtr.Add(interfaceListPtr, sizeof(uint) * 2);

                for (uint i = 0; i < numberOfItems; i++)
                {
                    var entryPtr = IntPtr.Add(arrayStart, (int)i * infoSize);
                    var info = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(entryPtr);
                    var interfaceGuid = info.InterfaceGuid;

                    var dataPtr = IntPtr.Zero;
                    try
                    {
                        var queryResult = WlanQueryInterface(
                            clientHandle,
                            ref interfaceGuid,
                            WlanIntfOpcodeCurrentConnection,
                            IntPtr.Zero,
                            out _,
                            out dataPtr,
                            IntPtr.Zero);

                        if (queryResult != ErrorSuccess || dataPtr == IntPtr.Zero)
                            continue;

                        var connection = Marshal.PtrToStructure<WLAN_CONNECTION_ATTRIBUTES>(dataPtr);
                        var ssidBytes = connection.wlanAssociationAttributes.dot11Ssid.ucSSID;
                        var ssidLength = (int)Math.Min(connection.wlanAssociationAttributes.dot11Ssid.uSSIDLength, 32);

                        if (ssidLength <= 0 || ssidBytes == null)
                            continue;

                        ssid = Encoding.UTF8.GetString(ssidBytes, 0, ssidLength);
                        return true;
                    }
                    finally
                    {
                        if (dataPtr != IntPtr.Zero)
                            WlanFreeMemory(dataPtr);
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (interfaceListPtr != IntPtr.Zero)
                    WlanFreeMemory(interfaceListPtr);
                if (clientHandle != IntPtr.Zero)
                    WlanCloseHandle(clientHandle, IntPtr.Zero);
            }
        }
    }
}
