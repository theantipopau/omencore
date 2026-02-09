using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    /// <summary>
    /// AMD GPU overclocking service using ADL (AMD Display Library).
    /// ADL is bundled with AMD Adrenalin drivers — no extra installation required.
    /// Supports clock offset, memory offset, and power limit tuning.
    /// </summary>
    public class AmdGpuService : IDisposable
    {
        private readonly LoggingService _logging;
        private bool _initialized;
        private bool _disposed;
        private IntPtr _adlContext = IntPtr.Zero;

        // ADL return codes
        private const int ADL_OK = 0;
        private const int ADL_ERR = -1;

        // ADL function delegates
        private delegate int ADL2_Main_Control_Create(ADL_Main_Memory_Alloc callback, int enumConnectedAdapters, ref IntPtr context);
        private delegate int ADL2_Main_Control_Destroy(IntPtr context);
        private delegate int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, ref int numAdapters);
        private delegate int ADL2_Adapter_AdapterInfo_Get(IntPtr context, IntPtr info, int inputSize);
        private delegate int ADL2_Overdrive8_Init_Setting_Get(IntPtr context, int adapterIndex, ref ADLOD8InitSetting initSettings);
        private delegate int ADL2_Overdrive8_Current_Setting_Get(IntPtr context, int adapterIndex, ref ADLOD8CurrentSetting currentSettings);
        private delegate int ADL2_Overdrive8_Setting_Set(IntPtr context, int adapterIndex, ref ADLOD8SetSetting settings, ref ADLOD8CurrentSetting result);
        private delegate IntPtr ADL_Main_Memory_Alloc(int size);

        // Loaded function pointers
        private ADL2_Main_Control_Create? _adl2Create;
        private ADL2_Main_Control_Destroy? _adl2Destroy;
        private ADL2_Adapter_NumberOfAdapters_Get? _adl2NumAdapters;
        private ADL2_Overdrive8_Init_Setting_Get? _adl2Od8InitGet;
        private ADL2_Overdrive8_Current_Setting_Get? _adl2Od8CurrentGet;
        private ADL2_Overdrive8_Setting_Set? _adl2Od8Set;

        private IntPtr _adlModule = IntPtr.Zero;
        private int _primaryAdapterIndex = -1;

        // Overdrive8 feature IDs
        private const int OD8_GFXCLK_FREQ1 = 0;      // GFX Clock frequency 1 (min)
        private const int OD8_GFXCLK_FREQ2 = 1;      // GFX Clock frequency 2 (mid) 
        private const int OD8_GFXCLK_FREQ3 = 2;      // GFX Clock frequency 3 (max)
        private const int OD8_GFXCLK_FMIN = 3;        // GFX Clock min frequency
        private const int OD8_GFXCLK_FMAX = 4;        // GFX Clock max frequency
        private const int OD8_UCLK_FMAX = 5;          // Memory clock max frequency
        private const int OD8_POWER_PERCENTAGE = 7;    // Power percentage limit
        private const int OD8_FAN_MIN_SPEED = 8;       // Fan min speed
        private const int OD8_FAN_TARGET_TEMP = 9;     // Fan target temperature
        private const int OD8_OPERATING_TEMP_MAX = 10;  // Max operating temperature
        private const int OD8_FAN_CURVE_SPEED_1 = 14;  // Fan curve speed 1
        
        // Public properties
        public bool IsAvailable => _initialized;
        public string GpuName { get; private set; } = "Not detected";
        public int CoreClockOffsetMHz { get; private set; }
        public int MemoryClockOffsetMHz { get; private set; }
        public int PowerLimitPercent { get; private set; }
        public int MaxCoreClockMHz { get; private set; }
        public int MaxMemoryClockMHz { get; private set; }
        public int MinPowerLimit { get; private set; }
        public int MaxPowerLimit { get; private set; }

        public AmdGpuService(LoggingService logging)
        {
            _logging = logging;
        }

        /// <summary>
        /// Initialize ADL2 and detect AMD discrete GPU.
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logging.Info("Initializing AMD GPU service (ADL2)...");

                    // Try loading atiadlxx.dll (64-bit) or atiadlxy.dll
                    _adlModule = NativeLibrary.Load("atiadlxx");

                    if (_adlModule == IntPtr.Zero)
                    {
                        _logging.Info("AMD ADL library not found - AMD Adrenalin drivers not installed");
                        return false;
                    }

                    // Resolve function pointers
                    if (!LoadAdlFunctions())
                    {
                        _logging.Warn("Failed to resolve ADL2 function pointers");
                        return false;
                    }

                    // Create ADL2 context
                    int result = _adl2Create!(ManagedAlloc, 1, ref _adlContext);
                    if (result != ADL_OK)
                    {
                        _logging.Warn($"ADL2_Main_Control_Create failed: {result}");
                        return false;
                    }

                    // Find primary discrete AMD GPU adapter
                    int numAdapters = 0;
                    _adl2NumAdapters!(_adlContext, ref numAdapters);

                    if (numAdapters <= 0)
                    {
                        _logging.Info("No AMD GPU adapters found");
                        return false;
                    }

                    // For OMEN laptops, find the discrete GPU (not the iGPU)
                    _primaryAdapterIndex = FindDiscreteGpu(numAdapters);
                    if (_primaryAdapterIndex < 0)
                    {
                        _logging.Info("No AMD discrete GPU found (only iGPU detected)");
                        return false;
                    }

                    // Read initial OD8 settings to get limits
                    ReadOverdriveLimits();

                    _initialized = true;
                    _logging.Info($"✓ AMD GPU service initialized: {GpuName} (adapter {_primaryAdapterIndex})");
                    return true;
                }
                catch (DllNotFoundException)
                {
                    _logging.Info("AMD ADL library not available - no AMD drivers installed");
                    return false;
                }
                catch (Exception ex)
                {
                    _logging.Warn($"AMD GPU service initialization failed: {ex.Message}");
                    return false;
                }
            });
        }

        private bool LoadAdlFunctions()
        {
            try
            {
                _adl2Create = GetDelegate<ADL2_Main_Control_Create>("ADL2_Main_Control_Create");
                _adl2Destroy = GetDelegate<ADL2_Main_Control_Destroy>("ADL2_Main_Control_Destroy");
                _adl2NumAdapters = GetDelegate<ADL2_Adapter_NumberOfAdapters_Get>("ADL2_Adapter_NumberOfAdapters_Get");
                _adl2Od8InitGet = GetDelegate<ADL2_Overdrive8_Init_Setting_Get>("ADL2_Overdrive8_Init_Setting_Get");
                _adl2Od8CurrentGet = GetDelegate<ADL2_Overdrive8_Current_Setting_Get>("ADL2_Overdrive8_Current_Setting_Get");
                _adl2Od8Set = GetDelegate<ADL2_Overdrive8_Setting_Set>("ADL2_Overdrive8_Setting_Set");

                return _adl2Create != null && _adl2Destroy != null && _adl2NumAdapters != null;
            }
            catch
            {
                return false;
            }
        }

        private T? GetDelegate<T>(string funcName) where T : Delegate
        {
            if (_adlModule == IntPtr.Zero) return null;
            
            if (NativeLibrary.TryGetExport(_adlModule, funcName, out IntPtr funcPtr))
            {
                return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);
            }
            return null;
        }

        private int FindDiscreteGpu(int numAdapters)
        {
            // Use adapter index 0 as default — on OMEN systems this is typically the discrete GPU
            // ADL sorts adapters by PCI bus, discrete GPU usually has index 0
            _logging.Info($"AMD GPU: Found {numAdapters} adapter(s)");
            GpuName = "AMD Radeon GPU";
            return 0;
        }

        private void ReadOverdriveLimits()
        {
            if (_adl2Od8InitGet == null) return;

            try
            {
                var initSettings = new ADLOD8InitSetting();
                int result = _adl2Od8InitGet(_adlContext, _primaryAdapterIndex, ref initSettings);

                if (result == ADL_OK)
                {
                    MaxCoreClockMHz = initSettings.od8SettingTable_GfxclkFmax_Default;
                    MaxMemoryClockMHz = initSettings.od8SettingTable_UclkFmax_Default;
                    MinPowerLimit = initSettings.od8SettingTable_PowerPercentage_Min;
                    MaxPowerLimit = initSettings.od8SettingTable_PowerPercentage_Max;
                    
                    _logging.Info($"AMD OD8 limits: Core max={MaxCoreClockMHz}MHz, Mem max={MaxMemoryClockMHz}MHz, Power={MinPowerLimit}-{MaxPowerLimit}%");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to read OD8 limits: {ex.Message}");
            }
        }

        /// <summary>
        /// Set GPU core clock offset in MHz.
        /// </summary>
        public bool SetCoreClockOffset(int offsetMHz)
        {
            if (!_initialized || _adl2Od8Set == null)
            {
                _logging.Warn("AMD GPU: Cannot set core clock - service not initialized");
                return false;
            }

            try
            {
                // Clamp to reasonable range
                offsetMHz = Math.Clamp(offsetMHz, -500, 500);

                var settings = new ADLOD8SetSetting();
                settings.count = 1;
                settings.od8SettingTable_Id_0 = OD8_GFXCLK_FMAX;
                settings.od8SettingTable_Value_0 = MaxCoreClockMHz + offsetMHz;

                var result_settings = new ADLOD8CurrentSetting();
                int result = _adl2Od8Set(_adlContext, _primaryAdapterIndex, ref settings, ref result_settings);

                if (result == ADL_OK)
                {
                    CoreClockOffsetMHz = offsetMHz;
                    _logging.Info($"✓ AMD GPU core clock offset set to {offsetMHz} MHz (target: {MaxCoreClockMHz + offsetMHz} MHz)");
                    return true;
                }
                else
                {
                    _logging.Warn($"AMD GPU: SetCoreClockOffset failed with code {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"AMD GPU: Failed to set core clock offset: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Set GPU memory clock offset in MHz.
        /// </summary>
        public bool SetMemoryClockOffset(int offsetMHz)
        {
            if (!_initialized || _adl2Od8Set == null)
            {
                _logging.Warn("AMD GPU: Cannot set memory clock - service not initialized");
                return false;
            }

            try
            {
                offsetMHz = Math.Clamp(offsetMHz, -500, 500);

                var settings = new ADLOD8SetSetting();
                settings.count = 1;
                settings.od8SettingTable_Id_0 = OD8_UCLK_FMAX;
                settings.od8SettingTable_Value_0 = MaxMemoryClockMHz + offsetMHz;

                var result_settings = new ADLOD8CurrentSetting();
                int result = _adl2Od8Set(_adlContext, _primaryAdapterIndex, ref settings, ref result_settings);

                if (result == ADL_OK)
                {
                    MemoryClockOffsetMHz = offsetMHz;
                    _logging.Info($"✓ AMD GPU memory clock offset set to {offsetMHz} MHz (target: {MaxMemoryClockMHz + offsetMHz} MHz)");
                    return true;
                }
                else
                {
                    _logging.Warn($"AMD GPU: SetMemoryClockOffset failed with code {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"AMD GPU: Failed to set memory clock offset: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Set GPU power limit as percentage offset (e.g., +15 = 115% TDP).
        /// </summary>
        public bool SetPowerLimit(int percentOffset)
        {
            if (!_initialized || _adl2Od8Set == null)
            {
                _logging.Warn("AMD GPU: Cannot set power limit - service not initialized");
                return false;
            }

            try
            {
                percentOffset = Math.Clamp(percentOffset, MinPowerLimit, MaxPowerLimit);

                var settings = new ADLOD8SetSetting();
                settings.count = 1;
                settings.od8SettingTable_Id_0 = OD8_POWER_PERCENTAGE;
                settings.od8SettingTable_Value_0 = percentOffset;

                var result_settings = new ADLOD8CurrentSetting();
                int result = _adl2Od8Set(_adlContext, _primaryAdapterIndex, ref settings, ref result_settings);

                if (result == ADL_OK)
                {
                    PowerLimitPercent = percentOffset;
                    _logging.Info($"✓ AMD GPU power limit set to {percentOffset}%");
                    return true;
                }
                else
                {
                    _logging.Warn($"AMD GPU: SetPowerLimit failed with code {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"AMD GPU: Failed to set power limit: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Reset all AMD GPU overclocking to defaults.
        /// </summary>
        public bool ResetToDefaults()
        {
            if (!_initialized) return false;

            bool success = true;
            success &= SetCoreClockOffset(0);
            success &= SetMemoryClockOffset(0);
            success &= SetPowerLimit(0);

            if (success)
                _logging.Info("✓ AMD GPU overclocking reset to defaults");
            else
                _logging.Warn("AMD GPU: Partial reset - some settings may not have been restored");

            return success;
        }

        private static IntPtr ManagedAlloc(int size)
        {
            return Marshal.AllocHGlobal(size);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_adl2Destroy != null && _adlContext != IntPtr.Zero)
            {
                try { _adl2Destroy(_adlContext); } catch { }
                _adlContext = IntPtr.Zero;
            }

            if (_adlModule != IntPtr.Zero)
            {
                NativeLibrary.Free(_adlModule);
                _adlModule = IntPtr.Zero;
            }
        }

        #region ADL Structures

        [StructLayout(LayoutKind.Sequential)]
        private struct ADLOD8InitSetting
        {
            public int count;
            public int overdrive8Capabilities;
            // Simplified — only the fields we need
            public int od8SettingTable_GfxclkFmax_Default;
            public int od8SettingTable_GfxclkFmax_Min;
            public int od8SettingTable_GfxclkFmax_Max;
            public int od8SettingTable_UclkFmax_Default;
            public int od8SettingTable_UclkFmax_Min;
            public int od8SettingTable_UclkFmax_Max;
            public int od8SettingTable_PowerPercentage_Default;
            public int od8SettingTable_PowerPercentage_Min;
            public int od8SettingTable_PowerPercentage_Max;
            // Padding for full struct
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public int[] reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ADLOD8CurrentSetting
        {
            public int count;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public int[] od8SettingTable;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ADLOD8SetSetting
        {
            public int count;
            // Setting pairs (id, value) — up to 32
            public int od8SettingTable_Id_0;
            public int od8SettingTable_Value_0;
            public int od8SettingTable_Id_1;
            public int od8SettingTable_Value_1;
            public int od8SettingTable_Id_2;
            public int od8SettingTable_Value_2;
            // Padding
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 58)]
            public int[] reserved;
        }

        #endregion
    }
}
