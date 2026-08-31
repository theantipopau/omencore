using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Device power state, as reported by CM_POWER_DATA.PD_MostRecentPowerState.
    /// </summary>
    public enum GpuPowerState
    {
        Unknown = 0,
        D0 = 1,
        D1 = 2,
        D2 = 3,
        D3 = 4
    }

    /// <summary>
    /// Reads a discrete GPU's current power state <b>without waking it</b>.
    ///
    /// On an Optimus/RTD3 laptop the dGPU parks in D3 whenever nothing is rendering, and
    /// any NVAPI query pulls it back to D0 for the driver's inactivity timeout. A monitoring
    /// loop polling faster than that timeout therefore prevents the state it is trying to
    /// report: the GPU never idles, and the telemetry saying so is the reason why.
    ///
    /// DEVPKEY_Device_PowerData is answered from the PnP manager's own bookkeeping and does
    /// not touch the device, so it can be called at any cadence. It is the only reading of
    /// dGPU power state that is safe to take in a poll loop — nvidia-smi and NVAPI both wake
    /// the device to answer, then report the P-state and wattage of a GPU that was asleep
    /// until they asked.
    ///
    /// Every failure path returns <see cref="GpuPowerState.Unknown"/> so callers fall back to
    /// polling normally. Suspending telemetry because a probe broke would be a worse bug than
    /// the one this fixes.
    ///
    /// Deliberately dependency-free — only cfgmgr32 P/Invoke — so it can be linked into the
    /// hardware worker, which does not reference the app assembly.
    /// </summary>
    public sealed class GpuPowerStateProbe
    {
        // DEVPKEY_Device_PowerData and DEVPKEY_Device_ClassGuid share the DEVPKEY_Device fmtid.
        private static readonly Guid DevpkeyDeviceFmtid =
            new("a45c254e-df1c-4efd-8020-67d146a850e0");
        private const uint PidClassGuid = 10;
        private const uint PidPowerData = 32;

        private static readonly Guid GuidDevclassDisplay =
            new("4d36e968-e325-11ce-bfc1-08002be10318");

        private const int CrSuccess = 0;
        private const uint CmLocateDevnodeNormal = 0;
        private const uint CmGetidlistFilterEnumerator = 0x00000001;
        private const uint CmGetidlistFilterPresent = 0x00000100;

        // CM_POWER_DATA: PD_Size is the first DWORD, PD_MostRecentPowerState the second.
        private const int MostRecentPowerStateOffset = 4;
        private const int MinimumPowerDataLength = MostRecentPowerStateOffset + sizeof(uint);

        private readonly Action<string>? _log;
        private readonly string _instanceIdPrefix;
        private readonly object _sync = new();

        private string? _cachedInstanceId;
        private bool _noSuchAdapter;
        private GpuPowerState _lastLoggedState = GpuPowerState.Unknown;

        /// <param name="log">Called only on state transitions, not per poll.</param>
        /// <param name="instanceIdPrefix">
        /// PnP instance-id prefix of the adapter to watch. Defaults to NVIDIA's PCI vendor id;
        /// the class itself is vendor-agnostic.
        /// </param>
        public GpuPowerStateProbe(Action<string>? log = null,
                                  string instanceIdPrefix = @"PCI\VEN_10DE")
        {
            _log = log;
            _instanceIdPrefix = instanceIdPrefix;
        }

        /// <summary>
        /// True when the adapter is known to be in D3. False when it is awake <i>or</i> when
        /// the state could not be determined — see the class remarks on failing open.
        /// </summary>
        public bool IsAsleep() => Read() == GpuPowerState.D3;

        /// <summary>
        /// Current power state, or <see cref="GpuPowerState.Unknown"/> if it cannot be read.
        /// </summary>
        public GpuPowerState Read()
        {
            if (!OperatingSystem.IsWindows()) return GpuPowerState.Unknown;

            try
            {
                var instanceId = ResolveInstanceId();
                if (instanceId == null) return GpuPowerState.Unknown;

                var state = ReadPowerState(instanceId);

                // The devnode can go stale across a driver restart or a GPU mode switch.
                // One re-resolve, then give up for this call.
                if (state == GpuPowerState.Unknown)
                {
                    lock (_sync) { _cachedInstanceId = null; }
                    instanceId = ResolveInstanceId();
                    if (instanceId != null) state = ReadPowerState(instanceId);
                }

                LogTransition(state);
                return state;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"GpuPowerStateProbe: read failed: {ex.Message}");
                return GpuPowerState.Unknown;
            }
        }

        private void LogTransition(GpuPowerState state)
        {
            lock (_sync)
            {
                if (state == _lastLoggedState) return;
                _lastLoggedState = state;
            }

            _log?.Invoke(state == GpuPowerState.D3
                ? "dGPU entered D3 — suspending GPU telemetry polling so it can stay there"
                : $"dGPU power state now {state} — GPU telemetry polling active");
        }

        private string? ResolveInstanceId()
        {
            lock (_sync)
            {
                if (_cachedInstanceId != null) return _cachedInstanceId;
                if (_noSuchAdapter) return null;
            }

            string? found = null;
            try
            {
                foreach (var id in EnumeratePciDeviceIds())
                {
                    if (!id.StartsWith(_instanceIdPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // The vendor id alone is not enough: NVIDIA's HDMI audio function is a
                    // separate PCI device under the same vendor. Match the display class.
                    if (!IsDisplayClass(id)) continue;

                    found = id;
                    break;
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"GpuPowerStateProbe: device discovery failed: {ex.Message}");
                return null;
            }

            lock (_sync)
            {
                _cachedInstanceId = found;
                // Latch only a clean "no such adapter". A discovery that threw returns above
                // without latching, so a transient failure does not disable the probe for the
                // lifetime of the process.
                if (found == null) _noSuchAdapter = true;
                return found;
            }
        }

        private static IEnumerable<string> EnumeratePciDeviceIds()
        {
            const uint flags = CmGetidlistFilterEnumerator | CmGetidlistFilterPresent;

            if (CM_Get_Device_ID_List_SizeW(out var length, "PCI", flags) != CrSuccess || length == 0)
                yield break;

            var buffer = new char[length];
            if (CM_Get_Device_ID_ListW("PCI", buffer, length, flags) != CrSuccess)
                yield break;

            // REG_MULTI_SZ: NUL-separated, double-NUL terminated.
            var start = 0;
            for (var i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] != '\0') continue;
                if (i > start) yield return new string(buffer, start, i - start);
                start = i + 1;
            }
        }

        private static bool IsDisplayClass(string instanceId)
        {
            if (CM_Locate_DevNodeW(out var devInst, instanceId, CmLocateDevnodeNormal) != CrSuccess)
                return false;

            var key = new DEVPROPKEY { fmtid = DevpkeyDeviceFmtid, pid = PidClassGuid };
            var buffer = new byte[16];
            var size = (uint)buffer.Length;

            if (CM_Get_DevNode_PropertyW(devInst, ref key, out _, buffer, ref size, 0) != CrSuccess)
                return false;
            if (size < 16) return false;

            return new Guid(buffer) == GuidDevclassDisplay;
        }

        private static GpuPowerState ReadPowerState(string instanceId)
        {
            if (CM_Locate_DevNodeW(out var devInst, instanceId, CmLocateDevnodeNormal) != CrSuccess)
                return GpuPowerState.Unknown;

            var key = new DEVPROPKEY { fmtid = DevpkeyDeviceFmtid, pid = PidPowerData };
            var buffer = new byte[128];
            var size = (uint)buffer.Length;

            if (CM_Get_DevNode_PropertyW(devInst, ref key, out _, buffer, ref size, 0) != CrSuccess)
                return GpuPowerState.Unknown;

            return ParseMostRecentPowerState(buffer, (int)size);
        }

        /// <summary>
        /// Pulls PD_MostRecentPowerState out of a CM_POWER_DATA blob. Split out so the
        /// decoding is testable without an NVIDIA adapter present.
        /// </summary>
        internal static GpuPowerState ParseMostRecentPowerState(byte[] buffer, int length)
        {
            if (buffer == null || length < MinimumPowerDataLength) return GpuPowerState.Unknown;

            var raw = BitConverter.ToUInt32(buffer, MostRecentPowerStateOffset);

            return raw switch
            {
                1 => GpuPowerState.D0,
                2 => GpuPowerState.D1,
                3 => GpuPowerState.D2,
                4 => GpuPowerState.D3,
                _ => GpuPowerState.Unknown
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_DevNode_PropertyW(
            uint dnDevInst,
            ref DEVPROPKEY propertyKey,
            out ulong propertyType,
            byte[] propertyBuffer,
            ref uint propertyBufferSize,
            uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_Device_ID_List_SizeW(out uint pulLen, string pszFilter, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_Device_ID_ListW(string pszFilter, char[] buffer, uint bufferLen, uint ulFlags);
    }
}
