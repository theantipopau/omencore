using System;
using System.Management;
using OmenCore.Models;

namespace OmenCore.Hardware
{
    /// <summary>
    /// AMD Ryzen CPU detection and SMU address configuration.
    /// Based on G-Helper/UXTU implementation.
    /// Supports Zen1 through Strix Halo/Fire Range architectures.
    /// </summary>
    public static class RyzenControl
    {
        public static string CpuName { get; private set; } = string.Empty;
        public static string CpuModel { get; private set; } = string.Empty;
        public static RyzenFamily Family { get; private set; } = RyzenFamily.Unknown;

        private static bool _initialized;

        /// <summary>
        /// Initialize CPU detection. Call once at startup.
        /// </summary>
        public static void Init()
        {
            if (_initialized) return;

            try
            {
                using var searcher = new ManagementObjectSearcher("select * from Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    CpuName = obj["Name"]?.ToString() ?? string.Empty;
                    CpuModel = obj["Caption"]?.ToString() ?? string.Empty;
                    break;
                }
            }
            catch
            {
                CpuName = string.Empty;
                CpuModel = string.Empty;
            }

            Family = DetectFamily();
            _initialized = true;
        }

        /// <summary>
        /// Check if this is an AMD CPU.
        /// </summary>
        public static bool IsAmd()
        {
            if (!_initialized) Init();
            return CpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("Athlon", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Check if this CPU supports Curve Optimizer undervolting.
        /// </summary>
        public static bool SupportsUndervolt()
        {
            if (!_initialized) Init();
            
            // Supported: Ryzen AI MAX, Ryzen AI 9, Ryzen 9 HX, Ryzen 8000/7000/6000/4000 H-series
            return CpuName.Contains("RYZEN AI MAX", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("Ryzen AI 9", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("Ryzen 9", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("Ryzen 7", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("4900H", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("4800H", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("4600H", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("6900H", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("6800H", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("7945H", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("7845H", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("8945H", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("8940H", StringComparison.OrdinalIgnoreCase) ||  // Hawk Point (Issue #8)
                   CpuName.Contains("8845H", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("8840H", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("HX 370", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("HX 375", StringComparison.OrdinalIgnoreCase) ||
                   // Generic patterns for H-series mobile CPUs
                   (CpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) && 
                    (CpuName.Contains("H", StringComparison.OrdinalIgnoreCase) || 
                     CpuName.Contains("HX", StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Check if this CPU supports iGPU Curve Optimizer.
        /// </summary>
        public static bool SupportsIgpuUndervolt()
        {
            if (!_initialized) Init();
            
            return CpuName.Contains("RYZEN AI MAX", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("6900H", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("7945H", StringComparison.OrdinalIgnoreCase) ||
                   CpuName.Contains("7845H", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get full CPU info for display.
        /// </summary>
        public static RyzenCpuInfo GetCpuInfo()
        {
            if (!_initialized) Init();
            
            return new RyzenCpuInfo
            {
                CpuName = CpuName,
                CpuModel = CpuModel,
                Family = Family,
                SupportsUndervolt = SupportsUndervolt(),
                SupportsIgpuUndervolt = SupportsIgpuUndervolt()
            };
        }

        /// <summary>
        /// Detect CPU family from model string.
        /// </summary>
        private static RyzenFamily DetectFamily()
        {
            // Zen1/+ Desktop
            if (CpuModel.Contains("Model 1") || CpuModel.Contains("Model 8"))
                return RyzenFamily.Zen1Plus;

            // Family 23 (Zen to Zen2)
            if (CpuModel.Contains("Family 23"))
            {
                if (CpuModel.Contains("Model 17")) return RyzenFamily.Raven;
                if (CpuModel.Contains("Model 24")) return RyzenFamily.Picasso;
                if (CpuModel.Contains("Model 32")) return RyzenFamily.Dali;
            }

            // Family 25 (Zen3)
            if (CpuModel.Contains("Family 25"))
            {
                if (CpuModel.Contains("Model 33")) return RyzenFamily.Vermeer;
                if (CpuModel.Contains("Model 80")) return RyzenFamily.CezanneBarcelo;
                if (CpuModel.Contains("Model 63") || CpuModel.Contains("Model 68"))
                    return RyzenFamily.Rembrandt;
                if (CpuModel.Contains("Model 97")) return RyzenFamily.RaphaelDragonRange;
            }

            // Renoir/Lucienne detection
            if (CpuModel.Contains("Model 96") || CpuModel.Contains("Model 104"))
                return RyzenFamily.RenoirLucienne;

            // Van Gogh (Steam Deck)
            if (CpuModel.Contains("Model 144")) return RyzenFamily.VanGogh;

            // Phoenix (Ryzen 7040)
            if (CpuModel.Contains("Model 116") || CpuModel.Contains("Model 120"))
                return RyzenFamily.Phoenix;

            // Mendocino
            if (CpuModel.Contains("Model 160")) return RyzenFamily.Mendocino;

            // Hawk Point (Ryzen 8040)
            if (CpuModel.Contains("Model 117")) return RyzenFamily.HawkPoint;

            // Family 26 (Zen5+)
            if (CpuModel.Contains("Family 26"))
            {
                if (CpuModel.Contains("Model 36")) return RyzenFamily.StrixPoint;
                if (CpuModel.Contains("Model 112")) return RyzenFamily.StrixHalo;
                if (CpuModel.Contains("Model 68") && CpuName.Contains("HX"))
                    return RyzenFamily.FireRange;
            }

            return RyzenFamily.Unknown;
        }

        /// <summary>
        /// Configure SMU addresses for the detected CPU family.
        /// </summary>
        public static void ConfigureSmuAddresses(RyzenSmu smu)
        {
            if (!_initialized) Init();

            smu.SmuPciAddr = 0x00000000;
            smu.SmuOffsetAddr = 0xB8;
            smu.SmuOffsetData = 0xBC;

            switch (Family)
            {
                case RyzenFamily.Zen1Plus:
                    smu.Mp1AddrMsg = 0x3B10528;
                    smu.Mp1AddrRsp = 0x3B10564;
                    smu.Mp1AddrArg = 0x3B10598;
                    smu.PsmuAddrMsg = 0x3B1051C;
                    smu.PsmuAddrRsp = 0x3B10568;
                    smu.PsmuAddrArg = 0x3B10590;
                    break;

                case RyzenFamily.Raven:
                case RyzenFamily.Picasso:
                case RyzenFamily.Dali:
                case RyzenFamily.RenoirLucienne:
                case RyzenFamily.CezanneBarcelo:
                    smu.Mp1AddrMsg = 0x3B10528;
                    smu.Mp1AddrRsp = 0x3B10564;
                    smu.Mp1AddrArg = 0x3B10998;
                    smu.PsmuAddrMsg = 0x3B10A20;
                    smu.PsmuAddrRsp = 0x3B10A80;
                    smu.PsmuAddrArg = 0x3B10A88;
                    break;

                case RyzenFamily.VanGogh:
                case RyzenFamily.Rembrandt:
                case RyzenFamily.Phoenix:
                case RyzenFamily.Mendocino:
                case RyzenFamily.HawkPoint:
                case RyzenFamily.StrixHalo:
                    smu.Mp1AddrMsg = 0x3B10528;
                    smu.Mp1AddrRsp = 0x3B10578;
                    smu.Mp1AddrArg = 0x3B10998;
                    smu.PsmuAddrMsg = 0x3B10A20;
                    smu.PsmuAddrRsp = 0x3B10A80;
                    smu.PsmuAddrArg = 0x3B10A88;
                    break;

                case RyzenFamily.StrixPoint:
                    smu.Mp1AddrMsg = 0x3B10928;
                    smu.Mp1AddrRsp = 0x3B10978;
                    smu.Mp1AddrArg = 0x3B10998;
                    smu.PsmuAddrMsg = 0x3B10A20;
                    smu.PsmuAddrRsp = 0x3B10A80;
                    smu.PsmuAddrArg = 0x3B10A88;
                    break;

                case RyzenFamily.Matisse:
                case RyzenFamily.Vermeer:
                    smu.Mp1AddrMsg = 0x3B10530;
                    smu.Mp1AddrRsp = 0x3B1057C;
                    smu.Mp1AddrArg = 0x3B109C4;
                    smu.PsmuAddrMsg = 0x3B10524;
                    smu.PsmuAddrRsp = 0x3B10570;
                    smu.PsmuAddrArg = 0x3B10A40;
                    break;

                case RyzenFamily.RaphaelDragonRange:
                case RyzenFamily.FireRange:
                    smu.Mp1AddrMsg = 0x3B10530;
                    smu.Mp1AddrRsp = 0x3B1057C;
                    smu.Mp1AddrArg = 0x3B109C4;
                    smu.PsmuAddrMsg = 0x03B10524;
                    smu.PsmuAddrRsp = 0x03B10570;
                    smu.PsmuAddrArg = 0x03B10A40;
                    break;

                default:
                    // Unknown family - addresses may not work
                    smu.Mp1AddrMsg = 0;
                    smu.Mp1AddrRsp = 0;
                    smu.Mp1AddrArg = 0;
                    smu.PsmuAddrMsg = 0;
                    smu.PsmuAddrRsp = 0;
                    smu.PsmuAddrArg = 0;
                    break;
            }
        }
    }
}
