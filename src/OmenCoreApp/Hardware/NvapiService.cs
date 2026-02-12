using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using NvAPIWrapper;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;

namespace OmenCore.Hardware
{
    /// <summary>
    /// NVIDIA NVAPI wrapper for GPU overclocking and control.
    /// Provides access to clock offsets, power limits, and GPU monitoring.
    /// </summary>
    public class NvapiService : IDisposable
    {
        private readonly Services.LoggingService _logging;
        private bool _initialized;
        private bool _disposed;
        private NvPhysicalGpuHandle[] _gpuHandles = new NvPhysicalGpuHandle[NVAPI_MAX_PHYSICAL_GPUS];
        
        // NvAPIWrapper GPU reference for modern OC API
        private PhysicalGPU? _primaryGpu;

        #region NVAPI Constants

        private const int NVAPI_OK = 0;
        private const int NVAPI_ERROR = -1;
        private const int NVAPI_NO_IMPLEMENTATION = -7;
        private const int NVAPI_API_NOT_INITIALIZED = -9;
        private const int NVAPI_INVALID_ARGUMENT = -5;
        private const int NVAPI_MAX_PHYSICAL_GPUS = 64;
        private const int NVAPI_MAX_CLOCKS_PER_GPU = 0x120;
        private const int NVAPI_MAX_PSTATES20_PSTATES = 16;
        private const int NVAPI_MAX_PSTATES20_CLOCKS = 8;
        private const int NVAPI_MAX_PSTATES20_BASE_VOLTAGES = 4;
        private const int NV_GPU_CLOCK_FREQUENCIES_VER = 0x00020000 | (sizeof(int) * 4);

        // Performance state IDs
        private const int NVAPI_GPU_PERF_PSTATE_P0 = 0;  // Maximum performance
        private const int NVAPI_GPU_PERF_PSTATE_P8 = 8;  // Basic 2D
        private const int NVAPI_GPU_PERF_PSTATE_P12 = 12; // Idle

        // Clock domains
        private const int NVAPI_GPU_PUBLIC_CLOCK_GRAPHICS = 0;
        private const int NVAPI_GPU_PUBLIC_CLOCK_MEMORY = 4;
        private const int NVAPI_GPU_PUBLIC_CLOCK_PROCESSOR = 7;

        #endregion

        #region NVAPI Structures

        [StructLayout(LayoutKind.Sequential)]
        private struct NvPhysicalGpuHandle
        {
            public IntPtr Handle;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct NV_GPU_CLOCK_FREQUENCIES
        {
            public uint Version;
            public uint ClockType; // 0 = current, 1 = base, 2 = boost
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = NVAPI_MAX_CLOCKS_PER_GPU)]
            public NV_GPU_CLOCK_FREQUENCIES_DOMAIN[] Domain;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct NV_GPU_CLOCK_FREQUENCIES_DOMAIN
        {
            public uint bIsPresent; // 1 if present
            public uint frequency;   // kHz
        }

        // Delta parameter structure (12 bytes total)
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct NV_GPU_PSTATE20_DELTA
        {
            public int value;    // Current delta value (kHz or uV)
            public int min;      // Min allowed delta
            public int max;      // Max allowed delta
        }

        // Single frequency dependent info (4 bytes)
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct NV_GPU_PSTATE20_CLOCK_SINGLE
        {
            public uint frequencyKHz;
        }

        // Frequency range dependent info (24 bytes)
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct NV_GPU_PSTATE20_CLOCK_RANGE
        {
            public uint minFreqKHz;
            public uint maxFreqKHz;
            public uint voltageDomainId;
            public uint minVoltageUv;
            public uint maxVoltageUv;
        }

        // Clock entry V1 structure - Pack=8, explicit layout for union
        // Size: 4 (domain) + 4 (type) + 4 (flags) + 12 (delta) + 24 (union) = 48 bytes
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct NV_GPU_PSTATE20_CLOCK_ENTRY_V1
        {
            public uint domainId;           // Clock domain (0=Graphics, 4=Memory)
            public uint typeId;             // 0=single frequency, 1=range
            public uint flags;              // bit0 = bIsEditable
            public NV_GPU_PSTATE20_DELTA freqDelta;  // 12 bytes
            // ClockDependentInfo union - use the larger size (range = 24 bytes)
            public uint data0;
            public uint data1;
            public uint data2;
            public uint data3;
            public uint data4;
            public uint data5; // Padding to next 8-byte boundary
        }

        // Base voltage entry V1 structure 
        // Size: 4 (domain) + 4 (flags) + 4 (voltage) + 12 (delta) = 24 bytes
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct NV_GPU_PSTATE20_BASE_VOLTAGE_ENTRY_V1
        {
            public uint domainId;
            public uint flags;             // bit0 = bIsEditable
            public uint voltageUv;         // Voltage in microvolts
            public NV_GPU_PSTATE20_DELTA voltDelta;  // 12 bytes delta
        }

        // Performance state entry - holds clocks and voltages for one P-state
        // MaxClocks = 8, MaxBaseVoltages = 4
        private const int PSTATE20_MAX_CLOCKS = 8;
        private const int PSTATE20_MAX_VOLTAGES = 4;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct NV_GPU_PERF_PSTATES20_INFO_V2
        {
            public uint Version;
            public uint bIsEditable;       // 1 if P-states can be edited
            public uint numPstates;        // Number of P-states
            public uint numClocks;         // Clocks per P-state
            public uint numBaseVoltages;   // Base voltages per P-state
            
            // Note: Actual structure has variable-size arrays after this header
            // We use Marshal.AllocHGlobal for the full structure when needed
        }

        // Power policy structures
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct NV_GPU_POWER_POLICIES_INFO_ENTRY
        {
            public uint policyId;
            public uint minPower_mW;     // Minimum power in mW (as percentage * 1000)
            public uint defPower_mW;     // Default power
            public uint maxPower_mW;     // Maximum power
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct NV_GPU_POWER_POLICIES_INFO
        {
            public uint Version;
            public uint valid;           // Bitmask of valid entries
            public uint entryCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public NV_GPU_POWER_POLICIES_INFO_ENTRY[] entries;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct NV_GPU_POWER_POLICIES_STATUS_ENTRY
        {
            public uint policyId;
            public uint power_mW;        // Current power target as percentage * 1000
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct NV_GPU_POWER_POLICIES_STATUS
        {
            public uint Version;
            public uint count;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public NV_GPU_POWER_POLICIES_STATUS_ENTRY[] entries;
        }

        #endregion

        #region NVAPI Imports

        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NvAPI_QueryInterface(uint id);

        // Function IDs obtained from NVAPI SDK
        private const uint NvAPI_Initialize_ID = 0x0150E828;
        private const uint NvAPI_Unload_ID = 0xD22BDD7E;
        private const uint NvAPI_EnumPhysicalGPUs_ID = 0xE5AC921F;
        private const uint NvAPI_GPU_GetFullName_ID = 0xCEEE8E9F;
        private const uint NvAPI_GPU_GetThermalSettings_ID = 0xE3640A56;
        private const uint NvAPI_GPU_GetAllClockFrequencies_ID = 0xDCB616C3;
        private const uint NvAPI_GPU_GetPstates20_ID = 0x6FF81213;
        private const uint NvAPI_GPU_SetPstates20_ID = 0x0F4DAE6B;
        private const uint NvAPI_GPU_GetPowerPoliciesInfo_ID = 0x34206D86;
        private const uint NvAPI_GPU_GetPowerPoliciesStatus_ID = 0x70916171;
        private const uint NvAPI_GPU_SetPowerPoliciesStatus_ID = 0xAD95F5ED;
        private const uint NvAPI_GPU_ClientPowerPoliciesGetStatus_ID = 0x70916171;
        private const uint NvAPI_GPU_ClientPowerPoliciesSetStatus_ID = 0xAD95F5ED;
        private const uint NvAPI_GPU_ClientPowerPoliciesGetInfo_ID = 0x34206D86;

        // Delegate types for existing functions
        private delegate int NvAPI_InitializeDelegate();
        private delegate int NvAPI_UnloadDelegate();
        private delegate int NvAPI_EnumPhysicalGPUsDelegate([Out] NvPhysicalGpuHandle[] gpuHandles, out int gpuCount);
        private delegate int NvAPI_GPU_GetFullNameDelegate(NvPhysicalGpuHandle hPhysicalGpu, [MarshalAs(UnmanagedType.LPStr)] System.Text.StringBuilder szName);
        
        // Delegate types for OC functions
        private delegate int NvAPI_GPU_GetAllClockFrequenciesDelegate(NvPhysicalGpuHandle hPhysicalGpu, ref NV_GPU_CLOCK_FREQUENCIES pClkFreqs);
        private delegate int NvAPI_GPU_GetPstates20Delegate(NvPhysicalGpuHandle hPhysicalGpu, IntPtr pPstates20Info);
        private delegate int NvAPI_GPU_SetPstates20Delegate(NvPhysicalGpuHandle hPhysicalGpu, IntPtr pPstates20Info);
        private delegate int NvAPI_GPU_ClientPowerPoliciesGetInfoDelegate(NvPhysicalGpuHandle hPhysicalGpu, ref NV_GPU_POWER_POLICIES_INFO pPowerInfo);
        private delegate int NvAPI_GPU_ClientPowerPoliciesGetStatusDelegate(NvPhysicalGpuHandle hPhysicalGpu, ref NV_GPU_POWER_POLICIES_STATUS pPowerStatus);
        private delegate int NvAPI_GPU_ClientPowerPoliciesSetStatusDelegate(NvPhysicalGpuHandle hPhysicalGpu, ref NV_GPU_POWER_POLICIES_STATUS pPowerStatus);

        // Cached delegates
        private NvAPI_InitializeDelegate? _nvAPI_Initialize;
        private NvAPI_UnloadDelegate? _nvAPI_Unload;
        private NvAPI_EnumPhysicalGPUsDelegate? _nvAPI_EnumPhysicalGPUs;
        private NvAPI_GPU_GetFullNameDelegate? _nvAPI_GPU_GetFullName;
        private NvAPI_GPU_GetAllClockFrequenciesDelegate? _nvAPI_GPU_GetAllClockFrequencies;
        private NvAPI_GPU_GetPstates20Delegate? _nvAPI_GPU_GetPstates20;
        private NvAPI_GPU_SetPstates20Delegate? _nvAPI_GPU_SetPstates20;
        private NvAPI_GPU_ClientPowerPoliciesGetInfoDelegate? _nvAPI_GPU_ClientPowerPoliciesGetInfo;
        private NvAPI_GPU_ClientPowerPoliciesGetStatusDelegate? _nvAPI_GPU_ClientPowerPoliciesGetStatus;
        private NvAPI_GPU_ClientPowerPoliciesSetStatusDelegate? _nvAPI_GPU_ClientPowerPoliciesSetStatus;

        #endregion

        #region Properties

        /// <summary>Whether NVAPI is initialized and available.</summary>
        public bool IsAvailable => _initialized;

        /// <summary>Whether this GPU supports clock offset overclocking.</summary>
        public bool SupportsOverclocking { get; private set; }

        /// <summary>Number of NVIDIA GPUs detected.</summary>
        public int GpuCount { get; private set; }

        /// <summary>Current GPU core clock offset in MHz.</summary>
        public int CoreClockOffsetMHz { get; private set; }

        /// <summary>Current GPU memory clock offset in MHz.</summary>
        public int MemoryClockOffsetMHz { get; private set; }

        /// <summary>Current GPU core voltage offset in mV.</summary>
        public int VoltageOffsetMv { get; private set; }

        /// <summary>Current power limit percentage (100 = default TDP).</summary>
        public int PowerLimitPercent { get; private set; } = 100;

        /// <summary>Minimum allowed core clock offset.</summary>
        public int MinCoreOffset { get; private set; } = -500;

        /// <summary>Maximum allowed core clock offset.</summary>
        public int MaxCoreOffset { get; private set; } = 300;

        /// <summary>Minimum allowed memory clock offset.</summary>
        public int MinMemoryOffset { get; private set; } = -500;

        /// <summary>Maximum allowed memory clock offset.</summary>
        public int MaxMemoryOffset { get; private set; } = 1500;

        /// <summary>Minimum power limit percentage.</summary>
        public int MinPowerLimit { get; private set; } = 50;

        /// <summary>Maximum power limit percentage.</summary>
        public int MaxPowerLimit { get; private set; } = 125;

        /// <summary>Default power limit in watts.</summary>
        public int DefaultPowerLimitWatts { get; private set; }

        /// <summary>GPU name.</summary>
        public string GpuName { get; private set; } = "Unknown";

        #endregion

        public NvapiService(Services.LoggingService logging)
        {
            _logging = logging;
        }

        /// <summary>
        /// Initialize NVAPI and enumerate GPUs.
        /// </summary>
        public bool Initialize()
        {
            if (_initialized) return true;

            try
            {
                // Use NvAPIWrapper for modern initialization
                try
                {
                    NVIDIA.Initialize();
                    var gpus = PhysicalGPU.GetPhysicalGPUs();
                    
                    if (gpus.Length > 0)
                    {
                        _primaryGpu = gpus[0];
                        GpuName = _primaryGpu.FullName;
                        GpuCount = gpus.Length;
                        
                        _logging.Info($"NVAPI: Initialized via NvAPIWrapper, {GpuCount} GPU(s) found");
                        _logging.Info($"NVAPI: Primary GPU: {GpuName}");
                        
                        // Detect limits based on GPU type
                        DetectLimits();
                        
                        // Query power limits
                        QueryPowerLimitsWrapper();
                        
                        // Query OC support
                        QueryOcSupport();
                        
                        _initialized = true;
                        SupportsOverclocking = true;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logging.Warn($"NVAPI: NvAPIWrapper initialization failed: {ex.Message}, falling back to legacy");
                }
                
                // Fallback to legacy initialization
                var initPtr = NvAPI_QueryInterface(NvAPI_Initialize_ID);
                if (initPtr == IntPtr.Zero)
                {
                    _logging.Info("NVAPI: nvapi64.dll not found or Initialize not available");
                    return false;
                }

                _nvAPI_Initialize = Marshal.GetDelegateForFunctionPointer<NvAPI_InitializeDelegate>(initPtr);

                // Initialize NVAPI
                var result = _nvAPI_Initialize();
                if (result != NVAPI_OK)
                {
                    _logging.Warn($"NVAPI: Initialize failed with code {result}");
                    return false;
                }

                // Query other functions
                QueryNvapiDelegates();

                // Enumerate GPUs
                EnumerateGpus();

                _initialized = true;
                _logging.Info($"NVAPI: Initialized successfully (legacy), {GpuCount} GPU(s) found");
                return true;
            }
            catch (DllNotFoundException)
            {
                _logging.Info("NVAPI: nvapi64.dll not found - NVIDIA drivers not installed");
                return false;
            }
            catch (Exception ex)
            {
                _logging.Error($"NVAPI: Initialization failed: {ex.Message}", ex);
                return false;
            }
        }
        
        private void QueryPowerLimitsWrapper()
        {
            if (_primaryGpu == null) return;
            
            try
            {
                // Try to get power limit info via NvAPIWrapper
                var policies = GPUApi.ClientPowerPoliciesGetInfo(_primaryGpu.Handle);
                if (policies.PowerPolicyInfoEntries.Length > 0)
                {
                    var entry = policies.PowerPolicyInfoEntries[0];
                    MinPowerLimit = (int)(entry.MinimumPowerInPCM / 1000);
                    MaxPowerLimit = (int)(entry.MaximumPowerInPCM / 1000);
                    DefaultPowerLimitWatts = (int)(entry.DefaultPowerInPCM / 1000);
                    
                    _logging.Info($"NVAPI: Power limits - Min: {MinPowerLimit}%, Max: {MaxPowerLimit}%, Default: {DefaultPowerLimitWatts}%");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"NVAPI: Failed to query power limits via wrapper: {ex.Message}");
            }
        }
        
        private void QueryOcSupport()
        {
            if (_primaryGpu == null) return;
            
            try
            {
                // Try to get current P-states to verify OC support
                var pstates = GPUApi.GetPerformanceStates20(_primaryGpu.Handle);
                
                _logging.Info($"NVAPI: P-states info retrieved, {pstates.PerformanceStates.Length} states, {pstates.Clocks.Count} clock domains, {pstates.Voltages.Count} voltage domains");
                SupportsOverclocking = true;
            }
            catch (Exception ex)
            {
                _logging.Warn($"NVAPI: GetPerformanceStates20 failed: {ex.Message}");
                SupportsOverclocking = true; // Still try to set
            }
        }

        private void QueryNvapiDelegates()
        {
            var ptr = NvAPI_QueryInterface(NvAPI_Unload_ID);
            if (ptr != IntPtr.Zero)
                _nvAPI_Unload = Marshal.GetDelegateForFunctionPointer<NvAPI_UnloadDelegate>(ptr);

            ptr = NvAPI_QueryInterface(NvAPI_EnumPhysicalGPUs_ID);
            if (ptr != IntPtr.Zero)
                _nvAPI_EnumPhysicalGPUs = Marshal.GetDelegateForFunctionPointer<NvAPI_EnumPhysicalGPUsDelegate>(ptr);

            ptr = NvAPI_QueryInterface(NvAPI_GPU_GetFullName_ID);
            if (ptr != IntPtr.Zero)
                _nvAPI_GPU_GetFullName = Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GetFullNameDelegate>(ptr);

            // OC-specific functions
            ptr = NvAPI_QueryInterface(NvAPI_GPU_GetAllClockFrequencies_ID);
            if (ptr != IntPtr.Zero)
                _nvAPI_GPU_GetAllClockFrequencies = Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GetAllClockFrequenciesDelegate>(ptr);

            ptr = NvAPI_QueryInterface(NvAPI_GPU_GetPstates20_ID);
            if (ptr != IntPtr.Zero)
                _nvAPI_GPU_GetPstates20 = Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GetPstates20Delegate>(ptr);

            ptr = NvAPI_QueryInterface(NvAPI_GPU_SetPstates20_ID);
            if (ptr != IntPtr.Zero)
                _nvAPI_GPU_SetPstates20 = Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_SetPstates20Delegate>(ptr);

            ptr = NvAPI_QueryInterface(NvAPI_GPU_ClientPowerPoliciesGetInfo_ID);
            if (ptr != IntPtr.Zero)
                _nvAPI_GPU_ClientPowerPoliciesGetInfo = Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_ClientPowerPoliciesGetInfoDelegate>(ptr);

            ptr = NvAPI_QueryInterface(NvAPI_GPU_ClientPowerPoliciesGetStatus_ID);
            if (ptr != IntPtr.Zero)
                _nvAPI_GPU_ClientPowerPoliciesGetStatus = Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_ClientPowerPoliciesGetStatusDelegate>(ptr);

            ptr = NvAPI_QueryInterface(NvAPI_GPU_ClientPowerPoliciesSetStatus_ID);
            if (ptr != IntPtr.Zero)
                _nvAPI_GPU_ClientPowerPoliciesSetStatus = Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_ClientPowerPoliciesSetStatusDelegate>(ptr);
            
            _logging.Debug($"NVAPI delegates loaded: Clock={_nvAPI_GPU_GetAllClockFrequencies != null}, Pstates20Get={_nvAPI_GPU_GetPstates20 != null}, Pstates20Set={_nvAPI_GPU_SetPstates20 != null}, PowerInfo={_nvAPI_GPU_ClientPowerPoliciesGetInfo != null}, PowerSet={_nvAPI_GPU_ClientPowerPoliciesSetStatus != null}");
        }

        private void EnumerateGpus()
        {
            if (_nvAPI_EnumPhysicalGPUs == null) return;

            var result = _nvAPI_EnumPhysicalGPUs(_gpuHandles, out int count);

            if (result == NVAPI_OK)
            {
                GpuCount = count;

                // Get GPU name
                if (count > 0 && _nvAPI_GPU_GetFullName != null)
                {
                    var name = new System.Text.StringBuilder(64);
                    if (_nvAPI_GPU_GetFullName(_gpuHandles[0], name) == NVAPI_OK)
                    {
                        GpuName = name.ToString();
                        _logging.Info($"NVAPI: Primary GPU: {GpuName}");
                    }
                }

                // Detect offset limits (laptop GPUs often have restricted ranges)
                DetectLimits();
                
                // Query power limits
                QueryPowerLimits();
                
                // Query current clock offsets
                QueryCurrentOffsets();
            }
        }

        private void QueryPowerLimits()
        {
            if (_nvAPI_GPU_ClientPowerPoliciesGetInfo == null || GpuCount == 0) return;

            try
            {
                var powerInfo = new NV_GPU_POWER_POLICIES_INFO
                {
                    Version = (2 << 16) | (uint)Marshal.SizeOf<NV_GPU_POWER_POLICIES_INFO>(),
                    entries = new NV_GPU_POWER_POLICIES_INFO_ENTRY[4]
                };

                var result = _nvAPI_GPU_ClientPowerPoliciesGetInfo(_gpuHandles[0], ref powerInfo);
                if (result == NVAPI_OK && powerInfo.entryCount > 0)
                {
                    var entry = powerInfo.entries[0];
                    // Power values are percentage * 1000 (e.g., 100000 = 100%)
                    MinPowerLimit = (int)(entry.minPower_mW / 1000);
                    MaxPowerLimit = (int)(entry.maxPower_mW / 1000);
                    DefaultPowerLimitWatts = (int)(entry.defPower_mW / 1000);
                    
                    _logging.Info($"NVAPI: Power limits - Min: {MinPowerLimit}%, Max: {MaxPowerLimit}%, Default: {DefaultPowerLimitWatts}%");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"NVAPI: Failed to query power limits: {ex.Message}");
            }
        }

        private void QueryCurrentOffsets()
        {
            if (_nvAPI_GPU_GetPstates20 == null || GpuCount == 0) return;

            try
            {
                // Allocate buffer for Pstates20 info (variable-size structure)
                int bufferSize = 0x10000; // 64KB should be enough
                var buffer = Marshal.AllocHGlobal(bufferSize);
                
                try
                {
                    // Try V3 first (RTX 40 series and newer)
                    // V3 structure size is approximately 0xAD8
                    uint versionV3 = (3 << 16) | 0xAD8;
                    Marshal.WriteInt32(buffer, (int)versionV3);
                    
                    var result = _nvAPI_GPU_GetPstates20(_gpuHandles[0], buffer);
                    
                    if (result != NVAPI_OK)
                    {
                        // Try V2 (older GPUs)
                        uint versionV2 = (2 << 16) | 0x7D8;
                        Marshal.WriteInt32(buffer, (int)versionV2);
                        result = _nvAPI_GPU_GetPstates20(_gpuHandles[0], buffer);
                        
                        if (result == NVAPI_OK)
                        {
                            _pstatesVersion = 2;
                            _logging.Info("NVAPI: Using Pstates20 V2 (older GPU)");
                        }
                    }
                    else
                    {
                        _pstatesVersion = 3;
                        _logging.Info("NVAPI: Using Pstates20 V3 (RTX 40+ GPU)");
                    }
                    
                    if (result == NVAPI_OK)
                    {
                        // Parse P-state data to get current offsets
                        // The structure layout is complex - simplified parsing
                        uint bIsEditable = (uint)Marshal.ReadInt32(buffer, 4);
                        uint numPstates = (uint)Marshal.ReadInt32(buffer, 8);
                        uint numClocks = (uint)Marshal.ReadInt32(buffer, 12);
                        
                        _logging.Info($"NVAPI: P-states - Editable: {bIsEditable}, NumPstates: {numPstates}, NumClocks: {numClocks}");
                        
                        // Even if bIsEditable is 0, we can still try to set offsets
                        // Some GPUs report 0 but still allow setting via SetPstates20
                        SupportsOverclocking = true; // Assume supported if we got here
                        
                        if (bIsEditable == 0)
                        {
                            _logging.Info("NVAPI: bIsEditable=0, but will still attempt OC operations");
                        }
                    }
                    else
                    {
                        _logging.Warn($"NVAPI: GetPstates20 returned {result} (both V2 and V3 failed)");
                        
                        // Even if GetPstates20 fails, we can still try SetPstates20
                        // Some drivers allow setting without reading
                        SupportsOverclocking = _nvAPI_GPU_SetPstates20 != null;
                        if (SupportsOverclocking)
                        {
                            _pstatesVersion = 3; // Default to V3 for newer GPUs
                            _logging.Info("NVAPI: GetPstates20 failed but SetPstates20 available - will attempt OC");
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"NVAPI: Failed to query current offsets: {ex.Message}");
                // Still allow OC attempts
                SupportsOverclocking = _nvAPI_GPU_SetPstates20 != null;
            }
        }
        
        private int _pstatesVersion = 3; // Default to V3 for modern GPUs

        private void DetectLimits()
        {
            // Default limits for laptop GPUs (more conservative)
            // Desktop GPUs typically allow more headroom
            if (GpuName.Contains("Laptop", StringComparison.OrdinalIgnoreCase) ||
                GpuName.Contains("Max-Q", StringComparison.OrdinalIgnoreCase) ||
                GpuName.Contains("Mobile", StringComparison.OrdinalIgnoreCase))
            {
                MaxCoreOffset = 200;
                MaxMemoryOffset = 500;
                MaxPowerLimit = 115; // Laptop GPUs often locked
                _logging.Info("NVAPI: Detected laptop GPU - using conservative limits");
            }
            else
            {
                MaxCoreOffset = 300;
                MaxMemoryOffset = 1500;
                MaxPowerLimit = 125;
                _logging.Info("NVAPI: Desktop GPU limits applied");
            }
        }

        /// <summary>
        /// Set clock offset using NvAPIWrapper (proper structure handling for RTX 40+).
        /// Based on LenovoLegionToolkit implementation.
        /// </summary>
        private bool SetClockOffsetWrapper(PublicClockDomain domain, int deltaKHz)
        {
            if (_primaryGpu == null) return false;
            
            try
            {
                // Create clock entries array
                var clockEntries = new[]
                {
                    new PerformanceStates20ClockEntryV1(domain, new PerformanceStates20ParameterDelta(deltaKHz))
                };
                
                // Empty voltage entries (we're only setting clocks)
                var voltageEntries = Array.Empty<PerformanceStates20BaseVoltageEntryV1>();
                
                // Create P-state info for P0 (max performance state)
                var pstateInfo = new[]
                {
                    new PerformanceStates20InfoV1.PerformanceState20(PerformanceStateId.P0_3DPerformance, clockEntries, voltageEntries)
                };
                
                // Create the full overclock structure
                var overclock = new PerformanceStates20InfoV1(pstateInfo, 1, 0);
                
                // Apply the overclock
                GPUApi.SetPerformanceStates20(_primaryGpu.Handle, overclock);
                
                _logging.Debug($"NVAPI: SetClockOffsetWrapper success - domain={domain}, delta={deltaKHz}kHz");
                return true;
            }
            catch (Exception ex)
            {
                _logging.Warn($"NVAPI: SetClockOffsetWrapper failed: {ex.Message}");
                throw; // Re-throw to let caller try legacy path
            }
        }
        
        /// <summary>
        /// Set both core and memory offsets in a single call (more efficient).
        /// </summary>
        private bool SetBothClockOffsetsWrapper(int coreDeltaKHz, int memDeltaKHz)
        {
            if (_primaryGpu == null) return false;
            
            try
            {
                var clockEntries = new[]
                {
                    new PerformanceStates20ClockEntryV1(PublicClockDomain.Graphics, new PerformanceStates20ParameterDelta(coreDeltaKHz)),
                    new PerformanceStates20ClockEntryV1(PublicClockDomain.Memory, new PerformanceStates20ParameterDelta(memDeltaKHz))
                };
                
                var voltageEntries = Array.Empty<PerformanceStates20BaseVoltageEntryV1>();
                
                var pstateInfo = new[]
                {
                    new PerformanceStates20InfoV1.PerformanceState20(PerformanceStateId.P0_3DPerformance, clockEntries, voltageEntries)
                };
                
                var overclock = new PerformanceStates20InfoV1(pstateInfo, 2, 0);
                GPUApi.SetPerformanceStates20(_primaryGpu.Handle, overclock);
                
                _logging.Debug($"NVAPI: SetBothClockOffsetsWrapper success - core={coreDeltaKHz}kHz, mem={memDeltaKHz}kHz");
                return true;
            }
            catch (Exception ex)
            {
                _logging.Warn($"NVAPI: SetBothClockOffsetsWrapper failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Set GPU core clock offset.
        /// </summary>
        /// <param name="offsetMHz">Offset in MHz (negative = undervolt, positive = overclock)</param>
        /// <returns>True if successful</returns>
        public bool SetCoreClockOffset(int offsetMHz)
        {
            if (!_initialized)
            {
                _logging.Warn("NVAPI: Not initialized");
                return false;
            }

            if (!SupportsOverclocking)
            {
                _logging.Warn("NVAPI: GPU does not support clock offset overclocking");
                return false;
            }

            // Clamp to valid range and note if we trimmed
            var requested = offsetMHz;
            offsetMHz = Math.Clamp(offsetMHz, MinCoreOffset, MaxCoreOffset);
            if (offsetMHz != requested)
            {
                _logging.Warn($"NVAPI: Core offset clamped from {requested} to {offsetMHz} MHz (guardrail)");
            }

            try
            {
                _logging.Info($"NVAPI: Setting core clock offset to {offsetMHz} MHz");
                
                // Try NvAPIWrapper first (more reliable for RTX 40 series)
                if (_primaryGpu != null)
                {
                    try
                    {
                        var result = SetClockOffsetWrapper(PublicClockDomain.Graphics, offsetMHz * 1000);
                        if (result)
                        {
                            CoreClockOffsetMHz = offsetMHz;
                            _logging.Info($"NVAPI: Core clock offset set to {offsetMHz} MHz via NvAPIWrapper");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"NVAPI: NvAPIWrapper core OC failed: {ex.Message}, trying legacy");
                    }
                }
                
                // Fallback to legacy P/Invoke
                if (_nvAPI_GPU_SetPstates20 != null && GpuCount > 0)
                {
                    // Build Pstates20 structure for clock offset
                    var result = SetClockOffset(NVAPI_GPU_PUBLIC_CLOCK_GRAPHICS, offsetMHz * 1000); // kHz
                    
                    if (result == NVAPI_OK)
                    {
                        CoreClockOffsetMHz = offsetMHz;
                        _logging.Info($"NVAPI: Core clock offset set to {offsetMHz} MHz successfully");
                        return true;
                    }
                    else
                    {
                        _logging.Warn($"NVAPI: SetPstates20 failed with code {result}");
                        return false;
                    }
                }
                else
                {
                    _logging.Warn("NVAPI: SetPstates20 not available - core clock offset cannot be applied");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"NVAPI: Failed to set core clock offset: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Set GPU memory clock offset.
        /// </summary>
        /// <param name="offsetMHz">Offset in MHz</param>
        /// <returns>True if successful</returns>
        public bool SetMemoryClockOffset(int offsetMHz)
        {
            if (!_initialized)
            {
                _logging.Warn("NVAPI: Not initialized");
                return false;
            }

            if (!SupportsOverclocking)
            {
                _logging.Warn("NVAPI: GPU does not support clock offset overclocking");
                return false;
            }

            var requested = offsetMHz;
            offsetMHz = Math.Clamp(offsetMHz, MinMemoryOffset, MaxMemoryOffset);
            if (offsetMHz != requested)
            {
                _logging.Warn($"NVAPI: Memory offset clamped from {requested} to {offsetMHz} MHz (guardrail)");
            }

            try
            {
                _logging.Info($"NVAPI: Setting memory clock offset to {offsetMHz} MHz");
                
                // Try NvAPIWrapper first (more reliable for RTX 40 series)
                if (_primaryGpu != null)
                {
                    try
                    {
                        var result = SetClockOffsetWrapper(PublicClockDomain.Memory, offsetMHz * 1000);
                        if (result)
                        {
                            MemoryClockOffsetMHz = offsetMHz;
                            _logging.Info($"NVAPI: Memory clock offset set to {offsetMHz} MHz via NvAPIWrapper");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"NVAPI: NvAPIWrapper memory OC failed: {ex.Message}, trying legacy");
                    }
                }
                
                // Fallback to legacy P/Invoke
                if (_nvAPI_GPU_SetPstates20 != null && GpuCount > 0)
                {
                    var result = SetClockOffset(NVAPI_GPU_PUBLIC_CLOCK_MEMORY, offsetMHz * 1000); // kHz
                    
                    if (result == NVAPI_OK)
                    {
                        MemoryClockOffsetMHz = offsetMHz;
                        _logging.Info($"NVAPI: Memory clock offset set to {offsetMHz} MHz successfully");
                        return true;
                    }
                    else
                    {
                        _logging.Warn($"NVAPI: SetPstates20 for memory failed with code {result}");
                        return false;
                    }
                }
                else
                {
                    _logging.Warn("NVAPI: SetPstates20 not available - memory clock offset cannot be applied");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"NVAPI: Failed to set memory clock offset: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Set GPU core voltage offset for undervolting/overvolting.
        /// </summary>
        /// <param name="offsetMv">Offset in mV (negative = undervolt, positive = overvolt)</param>
        /// <returns>True if successful</returns>
        public bool SetVoltageOffset(int offsetMv)
        {
            if (!_initialized)
            {
                _logging.Warn("NVAPI: Not initialized");
                return false;
            }

            if (!SupportsOverclocking)
            {
                _logging.Warn("NVAPI: GPU does not support voltage offset control");
                return false;
            }

            // Clamp to safe range: -200mV to +100mV
            offsetMv = Math.Clamp(offsetMv, -200, 100);

            try
            {
                _logging.Info($"NVAPI: Setting core voltage offset to {offsetMv} mV");
                
                if (_nvAPI_GPU_SetPstates20 != null && GpuCount > 0)
                {
                    // Convert mV to µV
                    int offsetUv = offsetMv * 1000;
                    
                    // Domain 0 = core voltage
                    const int NVAPI_GPU_PERF_VOLTAGE_INFO_DOMAIN_CORE = 0;
                    
                    var result = SetVoltageOffsetInternal(NVAPI_GPU_PERF_VOLTAGE_INFO_DOMAIN_CORE, offsetUv);
                    
                    if (result == NVAPI_OK)
                    {
                        VoltageOffsetMv = offsetMv;
                        _logging.Info($"NVAPI: Voltage offset set to {offsetMv} mV successfully");
                        return true;
                    }
                    else
                    {
                        _logging.Warn($"NVAPI: SetPstates20 for voltage failed with code {result}");
                        return false;
                    }
                }
                else
                {
                    _logging.Warn("NVAPI: SetPstates20 not available - voltage offset cannot be applied");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"NVAPI: Failed to set voltage offset: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Internal method to set clock offset via Pstates20.
        /// Uses proper NV_GPU_PERF_PSTATES20_INFO structure layout.
        /// </summary>
        private int SetClockOffset(int domainId, int deltaKHz)
        {
            if (_nvAPI_GPU_SetPstates20 == null) return NVAPI_NO_IMPLEMENTATION;

            // Structure layout for NV_GPU_PERF_PSTATES20_INFO:
            // Header: Version(4) + Flags(4) + numPstates(4) + numClocks(4) + numBaseVoltages(4) = 20 bytes
            // Pstates array: 16 entries × PerformanceState20 size
            // PerformanceState20: StateId(4) + Flags(4) + 8 ClockEntries + 4 VoltageEntries
            // ClockEntry size: domainId(4) + typeId(4) + flags(4) + delta(12) + dependentInfo(24) = 48 bytes
            // VoltageEntry size: domainId(4) + flags(4) + voltage(4) + delta(12) = 24 bytes
            // Total per pstate: 8 + (8×48) + (4×24) = 8 + 384 + 96 = 488 bytes
            
            const int HEADER_SIZE = 20;
            const int CLOCK_ENTRY_SIZE = 48;  // Actual size with padding
            const int VOLTAGE_ENTRY_SIZE = 24;
            const int MAX_CLOCKS = 8;
            const int MAX_VOLTAGES = 4;
            const int PSTATE_HEADER_SIZE = 8; // StateId + Flags
            const int PSTATE_SIZE = PSTATE_HEADER_SIZE + (MAX_CLOCKS * CLOCK_ENTRY_SIZE) + (MAX_VOLTAGES * VOLTAGE_ENTRY_SIZE);

            int bufferSize = 0x10000;
            var buffer = Marshal.AllocHGlobal(bufferSize);
            
            try
            {
                // Zero the buffer
                for (int i = 0; i < bufferSize; i += IntPtr.Size)
                    Marshal.WriteIntPtr(buffer, i, IntPtr.Zero);

                // Use the detected version (V3 for RTX 40+, V2 for older)
                uint version = _pstatesVersion == 3 
                    ? (3u << 16) | 0xAD8u  // V3 structure size
                    : (2u << 16) | 0x7D8u; // V2 structure size
                    
                // Header
                Marshal.WriteInt32(buffer, 0, (int)version);  // Version
                Marshal.WriteInt32(buffer, 4, 1);             // Flags: bIsEditable = true  
                Marshal.WriteInt32(buffer, 8, 1);             // numPstates = 1 (P0)
                Marshal.WriteInt32(buffer, 12, 1);            // numClocks = 1
                Marshal.WriteInt32(buffer, 16, 0);            // numBaseVoltages = 0

                // First P-state entry (P0 = max performance)
                int pstateOffset = HEADER_SIZE;
                Marshal.WriteInt32(buffer, pstateOffset, NVAPI_GPU_PERF_PSTATE_P0);  // StateId
                Marshal.WriteInt32(buffer, pstateOffset + 4, 0);                      // Flags

                // First clock entry within the P-state
                int clockOffset = pstateOffset + PSTATE_HEADER_SIZE;
                
                // Clock entry structure:
                // domainId (4) + typeId (4) + flags (4) + delta.value (4) + delta.min (4) + delta.max (4) + dependentInfo (24)
                Marshal.WriteInt32(buffer, clockOffset + 0, domainId);      // domainId (0=Graphics, 4=Memory)
                Marshal.WriteInt32(buffer, clockOffset + 4, 0);             // typeId = 0 (single frequency)
                Marshal.WriteInt32(buffer, clockOffset + 8, 1);             // flags: bIsEditable = true
                Marshal.WriteInt32(buffer, clockOffset + 12, deltaKHz);     // delta.value (the offset we want)
                Marshal.WriteInt32(buffer, clockOffset + 16, -500000);      // delta.min (-500 MHz)
                Marshal.WriteInt32(buffer, clockOffset + 20, 500000);       // delta.max (+500 MHz)
                // dependentInfo follows but we leave it zeroed

                _logging.Debug($"NVAPI: SetClockOffset using V{_pstatesVersion}, domain={domainId}, delta={deltaKHz}kHz, clockOffset={clockOffset}");
                
                var result = _nvAPI_GPU_SetPstates20(_gpuHandles[0], buffer);
                
                // If V3 fails, try V2 as fallback
                if (result != NVAPI_OK && _pstatesVersion == 3)
                {
                    _logging.Debug($"NVAPI: V3 failed ({result}), trying V2 fallback");
                    version = (2u << 16) | 0x7D8u;
                    Marshal.WriteInt32(buffer, 0, (int)version);
                    result = _nvAPI_GPU_SetPstates20(_gpuHandles[0], buffer);
                    
                    if (result == NVAPI_OK)
                    {
                        _pstatesVersion = 2;
                        _logging.Info("NVAPI: Switched to V2 for clock offsets");
                    }
                }
                
                if (result != NVAPI_OK)
                {
                    _logging.Warn($"NVAPI: SetClockOffset failed with code {result}");
                }
                
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Internal method to set voltage offset via Pstates20.
        /// Uses proper NV_GPU_PERF_PSTATES20_INFO structure layout.
        /// </summary>
        private int SetVoltageOffsetInternal(int domainId, int deltaUv)
        {
            if (_nvAPI_GPU_SetPstates20 == null) return NVAPI_NO_IMPLEMENTATION;

            // Structure layout - see SetClockOffset for details
            const int HEADER_SIZE = 20;
            const int CLOCK_ENTRY_SIZE = 48;
            const int VOLTAGE_ENTRY_SIZE = 24;
            const int MAX_CLOCKS = 8;
            const int MAX_VOLTAGES = 4;
            const int PSTATE_HEADER_SIZE = 8;

            int bufferSize = 0x10000;
            var buffer = Marshal.AllocHGlobal(bufferSize);
            
            try
            {
                // Zero the buffer
                for (int i = 0; i < bufferSize; i += IntPtr.Size)
                    Marshal.WriteIntPtr(buffer, i, IntPtr.Zero);

                // Use the detected version
                uint version = _pstatesVersion == 3 
                    ? (3u << 16) | 0xAD8u  // V3 structure size
                    : (2u << 16) | 0x7D8u; // V2 structure size
                    
                // Header
                Marshal.WriteInt32(buffer, 0, (int)version);  // Version
                Marshal.WriteInt32(buffer, 4, 1);             // Flags: bIsEditable = true
                Marshal.WriteInt32(buffer, 8, 1);             // numPstates = 1 (P0)
                Marshal.WriteInt32(buffer, 12, 0);            // numClocks = 0 (not setting clocks)
                Marshal.WriteInt32(buffer, 16, 1);            // numBaseVoltages = 1

                // First P-state entry (P0)
                int pstateOffset = HEADER_SIZE;
                Marshal.WriteInt32(buffer, pstateOffset, NVAPI_GPU_PERF_PSTATE_P0);  // StateId
                Marshal.WriteInt32(buffer, pstateOffset + 4, 0);                      // Flags

                // Voltage entry comes after clock entries array (even if numClocks=0, the array space is reserved)
                int voltageOffset = pstateOffset + PSTATE_HEADER_SIZE + (MAX_CLOCKS * CLOCK_ENTRY_SIZE);
                
                // Voltage entry structure:
                // domainId (4) + flags (4) + voltage (4) + delta.value (4) + delta.min (4) + delta.max (4)
                Marshal.WriteInt32(buffer, voltageOffset + 0, domainId);    // domainId (0 = core voltage)
                Marshal.WriteInt32(buffer, voltageOffset + 4, 1);           // flags: bIsEditable = true
                Marshal.WriteInt32(buffer, voltageOffset + 8, 0);           // voltage (absolute, not used)
                Marshal.WriteInt32(buffer, voltageOffset + 12, deltaUv);    // delta.value (the offset we want)
                Marshal.WriteInt32(buffer, voltageOffset + 16, -200000);    // delta.min (-200 mV)
                Marshal.WriteInt32(buffer, voltageOffset + 20, 100000);     // delta.max (+100 mV)

                _logging.Debug($"NVAPI: SetVoltageOffset using V{_pstatesVersion}, domain={domainId}, delta={deltaUv}uV, voltageOffset={voltageOffset}");

                var result = _nvAPI_GPU_SetPstates20(_gpuHandles[0], buffer);
                
                if (result != NVAPI_OK)
                {
                    _logging.Warn($"NVAPI: SetVoltageOffset failed with code {result}");
                }
                
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Set GPU power limit as percentage of default TDP.
        /// </summary>
        /// <param name="percent">Percentage (e.g., 100 = default, 115 = +15%)</param>
        /// <returns>True if successful</returns>
        public bool SetPowerLimit(int percent)
        {
            if (!_initialized)
            {
                _logging.Warn("NVAPI: Not initialized");
                return false;
            }

            percent = Math.Clamp(percent, MinPowerLimit, MaxPowerLimit);

            try
            {
                _logging.Info($"NVAPI: Setting power limit to {percent}%");
                
                if (_nvAPI_GPU_ClientPowerPoliciesSetStatus != null && GpuCount > 0)
                {
                    var powerStatus = new NV_GPU_POWER_POLICIES_STATUS
                    {
                        Version = (1 << 16) | (uint)Marshal.SizeOf<NV_GPU_POWER_POLICIES_STATUS>(),
                        count = 1,
                        entries = new NV_GPU_POWER_POLICIES_STATUS_ENTRY[4]
                    };
                    
                    // Power is stored as percentage * 1000 (e.g., 115% = 115000)
                    powerStatus.entries[0] = new NV_GPU_POWER_POLICIES_STATUS_ENTRY
                    {
                        policyId = 0,
                        power_mW = (uint)(percent * 1000)
                    };

                    var result = _nvAPI_GPU_ClientPowerPoliciesSetStatus(_gpuHandles[0], ref powerStatus);
                    
                    if (result == NVAPI_OK)
                    {
                        PowerLimitPercent = percent;
                        _logging.Info($"NVAPI: Power limit set to {percent}% successfully");
                        return true;
                    }
                    else
                    {
                        _logging.Warn($"NVAPI: SetPowerPoliciesStatus failed with code {result}");
                        PowerLimitPercent = percent;
                        return false;
                    }
                }
                else
                {
                    PowerLimitPercent = percent;
                    _logging.Warn("NVAPI: Power policy API not available - value stored but not applied");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"NVAPI: Failed to set power limit: {ex.Message}", ex);
                return false;
            }
        }

        #region GPU Monitoring (Self-Sustaining Mode)

        /// <summary>
        /// Get current GPU utilization percentage via NvAPIWrapper.
        /// Returns -1 if unavailable.
        /// </summary>
        public int GetGpuLoad()
        {
            if (!_initialized || _primaryGpu == null) return -1;

            try
            {
                var gpuUsage = _primaryGpu.UsageInformation.GPU;
                return gpuUsage?.Percentage ?? -1;
            }
            catch (Exception ex)
            {
                _logging.Debug($"NVAPI: GetGpuLoad failed: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Get current GPU temperature via NvAPIWrapper thermal sensors.
        /// Returns 0 if unavailable.
        /// </summary>
        public double GetGpuTemperature()
        {
            if (!_initialized || _primaryGpu == null) return 0;

            try
            {
                var sensors = _primaryGpu.ThermalInformation.ThermalSensors;
                foreach (var sensor in sensors)
                {
                    if (sensor.Target == NvAPIWrapper.Native.GPU.ThermalSettingsTarget.GPU)
                    {
                        return sensor.CurrentTemperature;
                    }
                }
                // Fallback: return first sensor if no GPU-targeted sensor found
                foreach (var sensor in _primaryGpu.ThermalInformation.ThermalSensors)
                {
                    return sensor.CurrentTemperature;
                }
            }
            catch (Exception ex)
            {
                _logging.Debug($"NVAPI: GetGpuTemperature failed: {ex.Message}");
            }
            return 0;
        }

        /// <summary>
        /// Get GPU VRAM usage (used MB, total MB) via NvAPIWrapper.
        /// </summary>
        public (double UsedMb, double TotalMb) GetGpuVramUsage()
        {
            if (!_initialized || _primaryGpu == null) return (0, 0);

            try
            {
                var memInfo = _primaryGpu.MemoryInformation;
                double totalKb = memInfo.AvailableDedicatedVideoMemoryInkB;
                double availableKb = memInfo.CurrentAvailableDedicatedVideoMemoryInkB;
                double totalMb = totalKb / 1024.0;
                double usedMb = (totalKb - availableKb) / 1024.0;
                return (Math.Max(0, usedMb), totalMb);
            }
            catch (Exception ex)
            {
                _logging.Debug($"NVAPI: GetGpuVramUsage failed: {ex.Message}");
                return (0, 0);
            }
        }

        /// <summary>
        /// Get GPU power usage as percentage of TDP via NvAPIWrapper PowerTopology.
        /// Returns estimated watts = (percentage / 100) * DefaultPowerLimitWatts.
        /// </summary>
        public double GetGpuPowerWatts()
        {
            if (!_initialized || _primaryGpu == null) return 0;

            try
            {
                var entries = _primaryGpu.PowerTopologyInformation.PowerTopologyEntries;
                foreach (var entry in entries)
                {
                    // PowerUsageInPercent is relative to default TDP
                    double percent = entry.PowerUsageInPercent;
                    int effectiveTdp = DefaultPowerLimitWatts > 0 
                        ? DefaultPowerLimitWatts 
                        : EstimateFallbackTdp(GpuName);
                    
                    if (effectiveTdp > 0)
                    {
                        return Math.Round((percent / 100.0) * effectiveTdp, 1);
                    }
                    return percent; // Return percentage if we don't know TDP
                }
            }
            catch (Exception ex)
            {
                _logging.Debug($"NVAPI: GetGpuPowerWatts failed: {ex.Message}");
            }
            return 0;
        }

        /// <summary>
        /// Lightweight GPU monitoring — only load + VRAM (skips temp/clocks/power).
        /// Used when Afterburner is providing those metrics via shared memory,
        /// avoiding NVAPI polling contention on thermal/clock/power endpoints.
        /// </summary>
        public GpuMonitoringSample GetLoadAndVramOnly()
        {
            var sample = new GpuMonitoringSample { GpuName = GpuName };

            if (!_initialized || _primaryGpu == null) return sample;

            try
            {
                // GPU Load — lightweight call, minimal contention
                try
                {
                    var gpuUsage = _primaryGpu.UsageInformation.GPU;
                    sample.GpuLoadPercent = gpuUsage?.Percentage ?? 0;
                }
                catch { }

                // VRAM — lightweight call, reads driver-cached memory counters
                try
                {
                    var memInfo = _primaryGpu.MemoryInformation;
                    double totalKb = memInfo.AvailableDedicatedVideoMemoryInkB;
                    double availableKb = memInfo.CurrentAvailableDedicatedVideoMemoryInkB;
                    sample.VramTotalMb = totalKb / 1024.0;
                    sample.VramUsedMb = Math.Max(0, (totalKb - availableKb) / 1024.0);
                }
                catch { }
            }
            catch (Exception ex)
            {
                _logging.Debug($"NVAPI: GetLoadAndVramOnly failed: {ex.Message}");
            }

            return sample;
        }

        /// <summary>
        /// Comprehensive GPU monitoring snapshot — all metrics in one call.
        /// More efficient than calling individual methods as it handles exceptions once.
        /// </summary>
        public GpuMonitoringSample GetMonitoringSample()
        {
            var sample = new GpuMonitoringSample { GpuName = GpuName };

            if (!_initialized || _primaryGpu == null) return sample;

            try
            {
                // GPU Load
                try
                {
                    var gpuUsage = _primaryGpu.UsageInformation.GPU;
                    sample.GpuLoadPercent = gpuUsage?.Percentage ?? 0;
                }
                catch { }

                // GPU Temperature
                try
                {
                    foreach (var sensor in _primaryGpu.ThermalInformation.ThermalSensors)
                    {
                        if (sensor.Target == NvAPIWrapper.Native.GPU.ThermalSettingsTarget.GPU)
                        {
                            sample.GpuTemperatureC = sensor.CurrentTemperature;
                            break;
                        }
                        // Use first sensor as fallback
                        if (sample.GpuTemperatureC == 0)
                            sample.GpuTemperatureC = sensor.CurrentTemperature;
                    }
                }
                catch { }

                // VRAM
                try
                {
                    var memInfo = _primaryGpu.MemoryInformation;
                    double totalKb = memInfo.AvailableDedicatedVideoMemoryInkB;
                    double availableKb = memInfo.CurrentAvailableDedicatedVideoMemoryInkB;
                    sample.VramTotalMb = totalKb / 1024.0;
                    sample.VramUsedMb = Math.Max(0, (totalKb - availableKb) / 1024.0);
                }
                catch { }

                // Power
                try
                {
                    foreach (var entry in _primaryGpu.PowerTopologyInformation.PowerTopologyEntries)
                    {
                        double percent = entry.PowerUsageInPercent;
                        int effectiveTdp = DefaultPowerLimitWatts > 0 
                            ? DefaultPowerLimitWatts 
                            : EstimateFallbackTdp(GpuName);
                        
                        sample.GpuPowerWatts = effectiveTdp > 0
                            ? Math.Round((percent / 100.0) * effectiveTdp, 1)
                            : percent;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logging.Debug($"NVAPI: PowerTopology read failed: {ex.Message}");
                    
                    // Fallback: try standalone GetGpuPowerWatts() 
                    try
                    {
                        sample.GpuPowerWatts = GetGpuPowerWatts();
                    }
                    catch { }
                }

                // Clocks
                try
                {
                    var clocks = GetCurrentClocks();
                    sample.CoreClockMhz = clocks.CoreClockMHz;
                    sample.MemoryClockMhz = clocks.MemoryClockMHz;
                }
                catch { }
            }
            catch (Exception ex)
            {
                _logging.Debug($"NVAPI: GetMonitoringSample failed: {ex.Message}");
            }

            return sample;
        }

        #endregion

        /// <summary>
        /// Fallback TDP estimation for known laptop GPUs when NVAPI power limit query fails.
        /// Returns watts (default TDP, not max boost TDP).
        /// </summary>
        private static int EstimateFallbackTdp(string? gpuName)
        {
            if (string.IsNullOrEmpty(gpuName)) return 0;
            
            // Common NVIDIA laptop GPU TDPs (default power, not max boost)
            if (gpuName.Contains("4090", StringComparison.OrdinalIgnoreCase)) return 150;
            if (gpuName.Contains("4080", StringComparison.OrdinalIgnoreCase)) return 150;
            if (gpuName.Contains("4070", StringComparison.OrdinalIgnoreCase)) return 140;
            if (gpuName.Contains("4060", StringComparison.OrdinalIgnoreCase)) return 115;
            if (gpuName.Contains("4050", StringComparison.OrdinalIgnoreCase)) return 115;
            if (gpuName.Contains("3080", StringComparison.OrdinalIgnoreCase)) return 150;
            if (gpuName.Contains("3070", StringComparison.OrdinalIgnoreCase)) return 125;
            if (gpuName.Contains("3060", StringComparison.OrdinalIgnoreCase)) return 115;
            
            return 0; // Unknown GPU — return raw percentage
        }

        /// <summary>
        /// Get current GPU clock frequencies.
        /// </summary>
        public GpuClockInfo GetCurrentClocks()
        {
            var info = new GpuClockInfo
            {
                CoreOffsetMHz = CoreClockOffsetMHz,
                MemoryOffsetMHz = MemoryClockOffsetMHz
            };

            if (!_initialized || _nvAPI_GPU_GetAllClockFrequencies == null || GpuCount == 0)
                return info;

            try
            {
                var clockFreqs = new NV_GPU_CLOCK_FREQUENCIES
                {
                    Version = (2 << 16) | (uint)Marshal.SizeOf<NV_GPU_CLOCK_FREQUENCIES>(),
                    ClockType = 0, // Current clocks
                    Domain = new NV_GPU_CLOCK_FREQUENCIES_DOMAIN[NVAPI_MAX_CLOCKS_PER_GPU]
                };

                var result = _nvAPI_GPU_GetAllClockFrequencies(_gpuHandles[0], ref clockFreqs);
                if (result == NVAPI_OK)
                {
                    // Graphics clock (domain 0)
                    if (clockFreqs.Domain[NVAPI_GPU_PUBLIC_CLOCK_GRAPHICS].bIsPresent == 1)
                    {
                        info.CoreClockMHz = (int)(clockFreqs.Domain[NVAPI_GPU_PUBLIC_CLOCK_GRAPHICS].frequency / 1000);
                    }

                    // Memory clock (domain 4)
                    if (clockFreqs.Domain[NVAPI_GPU_PUBLIC_CLOCK_MEMORY].bIsPresent == 1)
                    {
                        info.MemoryClockMHz = (int)(clockFreqs.Domain[NVAPI_GPU_PUBLIC_CLOCK_MEMORY].frequency / 1000);
                    }
                }

                // Get boost clock
                clockFreqs.ClockType = 2; // Boost clocks
                result = _nvAPI_GPU_GetAllClockFrequencies(_gpuHandles[0], ref clockFreqs);
                if (result == NVAPI_OK && clockFreqs.Domain[NVAPI_GPU_PUBLIC_CLOCK_GRAPHICS].bIsPresent == 1)
                {
                    info.BoostClockMHz = (int)(clockFreqs.Domain[NVAPI_GPU_PUBLIC_CLOCK_GRAPHICS].frequency / 1000);
                }

                // Get base clock
                clockFreqs.ClockType = 1; // Base clocks
                result = _nvAPI_GPU_GetAllClockFrequencies(_gpuHandles[0], ref clockFreqs);
                if (result == NVAPI_OK && clockFreqs.Domain[NVAPI_GPU_PUBLIC_CLOCK_GRAPHICS].bIsPresent == 1)
                {
                    info.BaseClockMHz = (int)(clockFreqs.Domain[NVAPI_GPU_PUBLIC_CLOCK_GRAPHICS].frequency / 1000);
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"NVAPI: Failed to get clock frequencies: {ex.Message}");
            }

            return info;
        }

        /// <summary>
        /// Reset all overclocking settings to default.
        /// </summary>
        public bool ResetToDefaults()
        {
            try
            {
                SetCoreClockOffset(0);
                SetMemoryClockOffset(0);
                SetPowerLimit(100);
                
                _logging.Info("NVAPI: Reset all settings to defaults");
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"NVAPI: Failed to reset defaults: {ex.Message}", ex);
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                if (_initialized && _nvAPI_Unload != null)
                {
                    _nvAPI_Unload();
                    _logging.Info("NVAPI: Unloaded");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"NVAPI: Unload failed: {ex.Message}");
            }

            _disposed = true;
            _initialized = false;
        }
    }

    /// <summary>
    /// GPU clock frequency information.
    /// </summary>
    public class GpuClockInfo
    {
        public int CoreClockMHz { get; set; }
        public int MemoryClockMHz { get; set; }
        public int CoreOffsetMHz { get; set; }
        public int MemoryOffsetMHz { get; set; }
        public int BoostClockMHz { get; set; }
        public int BaseClockMHz { get; set; }
    }

    /// <summary>
    /// GPU overclocking profile.
    /// </summary>
    public class GpuOcProfile
    {
        public string Name { get; set; } = "Default";
        public int CoreOffsetMHz { get; set; }
        public int MemoryOffsetMHz { get; set; }
        public int PowerLimitPercent { get; set; } = 100;
        public int? VoltageOffsetMv { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// GPU monitoring snapshot from NVAPI — used by self-sustaining WmiBiosMonitor.
    /// </summary>
    public class GpuMonitoringSample
    {
        public string GpuName { get; set; } = "Unknown";
        public double GpuTemperatureC { get; set; }
        public double GpuLoadPercent { get; set; }
        public double GpuPowerWatts { get; set; }
        public double CoreClockMhz { get; set; }
        public double MemoryClockMhz { get; set; }
        public double VramUsedMb { get; set; }
        public double VramTotalMb { get; set; }
    }
}
