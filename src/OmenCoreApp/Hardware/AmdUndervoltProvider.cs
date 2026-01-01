using System;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Models;

namespace OmenCore.Hardware
{
    /// <summary>
    /// AMD Ryzen Curve Optimizer undervolting provider.
    /// Based on G-Helper/UXTU implementation.
    /// </summary>
    public class AmdUndervoltProvider : ICpuUndervoltProvider, IDisposable
    {
        private readonly object _stateLock = new();
        private readonly RyzenSmu _smu;
        private readonly RyzenCpuInfo _cpuInfo;
        
        private int _lastAllCoreCO;
        private int _lastIgpuCO;
        private bool _disposed;

        public string ActiveBackend { get; private set; } = "None";
        public bool IsSupported => _cpuInfo.SupportsUndervolt;
        public bool SupportsIgpu => _cpuInfo.SupportsIgpuUndervolt;
        public RyzenFamily Family => _cpuInfo.Family;
        public string CpuName => _cpuInfo.CpuName;

        public AmdUndervoltProvider()
        {
            _cpuInfo = RyzenControl.GetCpuInfo();
            _smu = new RyzenSmu();

            if (_smu.Initialize())
            {
                RyzenControl.ConfigureSmuAddresses(_smu);
                ActiveBackend = "PawnIO (SMU)";
            }
        }

        /// <summary>
        /// Apply undervolt using Intel-style offset model.
        /// Maps Core offset to All-Core Curve Optimizer.
        /// </summary>
        public Task ApplyOffsetAsync(UndervoltOffset offset, CancellationToken token)
        {
            // Convert Intel-style mV offset to Curve Optimizer units
            // CO is roughly 3-5mV per count, we'll approximate
            int coCounts = (int)(offset.CoreMv / 4.0);
            
            return ApplyRyzenOffsetAsync(coCounts, 0, token);
        }

        /// <summary>
        /// Apply AMD-native Curve Optimizer offset.
        /// </summary>
        public Task ApplyRyzenOffsetAsync(int allCoreCO, int igpuCO, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (_stateLock)
            {
                if (!_smu.IsAvailable)
                {
                    throw new InvalidOperationException("Ryzen SMU is not available. Install PawnIO driver.");
                }

                if (!_cpuInfo.SupportsUndervolt)
                {
                    throw new InvalidOperationException($"CPU {_cpuInfo.CpuName} does not support Curve Optimizer undervolting.");
                }

                // Apply All-Core CO
                var status = SetAllCoreCO(allCoreCO);
                if (status != RyzenSmu.SmuStatus.Ok)
                {
                    throw new InvalidOperationException($"Failed to set All-Core CO: {status}");
                }
                _lastAllCoreCO = allCoreCO;

                // Apply iGPU CO if supported
                if (_cpuInfo.SupportsIgpuUndervolt && igpuCO != 0)
                {
                    status = SetIgpuCO(igpuCO);
                    if (status != RyzenSmu.SmuStatus.Ok)
                    {
                        // iGPU CO failure is non-fatal
                    }
                    _lastIgpuCO = igpuCO;
                }
            }

            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (_stateLock)
            {
                if (_smu.IsAvailable && _cpuInfo.SupportsUndervolt)
                {
                    try
                    {
                        SetAllCoreCO(0);
                        if (_cpuInfo.SupportsIgpuUndervolt)
                        {
                            SetIgpuCO(0);
                        }
                    }
                    catch
                    {
                        // Ignore reset failures
                    }
                }

                _lastAllCoreCO = 0;
                _lastIgpuCO = 0;
            }

            return Task.CompletedTask;
        }

        public Task<UndervoltStatus> ProbeAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            lock (_stateLock)
            {
                var status = new UndervoltStatus
                {
                    // Convert CO to approximate mV (CO * ~4mV)
                    CurrentCoreOffsetMv = _lastAllCoreCO * 4,
                    CurrentCacheOffsetMv = _lastIgpuCO * 4, // Use cache field for iGPU
                    ControlledByOmenCore = true,
                    Timestamp = DateTime.Now
                };

                if (!_smu.IsAvailable)
                {
                    status.Warning = "Ryzen SMU not available. Install PawnIO driver for AMD CPU undervolting.";
                    status.ControlledByOmenCore = false;
                }
                else if (!_cpuInfo.SupportsUndervolt)
                {
                    status.Warning = $"CPU {_cpuInfo.CpuName} may not support Curve Optimizer. Undervolting may not work.";
                }
                else if (_cpuInfo.Family == RyzenFamily.Unknown)
                {
                    status.Warning = $"Unknown AMD CPU family. SMU addresses may be incorrect.";
                }

                return Task.FromResult(status);
            }
        }

        /// <summary>
        /// Set All-Core Curve Optimizer offset.
        /// Negative values = undervolt.
        /// </summary>
        private RyzenSmu.SmuStatus SetAllCoreCO(int value)
        {
            // Convert signed offset to SMU format
            // Formula from G-Helper: 0x100000 - (uint)(-1 * value) for negative values
            uint uvalue = value < 0 
                ? (uint)(0x100000 - (uint)(-value))
                : (uint)value;

            uint[] args = new uint[6];
            args[0] = uvalue;
            RyzenSmu.SmuStatus result = RyzenSmu.SmuStatus.Failed;

            switch (_cpuInfo.Family)
            {
                case RyzenFamily.RenoirLucienne:
                case RyzenFamily.CezanneBarcelo:
                    result = _smu.SendMp1(0x55, ref args);
                    if (result == RyzenSmu.SmuStatus.Ok)
                        result = _smu.SendPsmu(0xB1, ref args);
                    break;

                case RyzenFamily.Matisse:
                case RyzenFamily.Vermeer:
                    result = _smu.SendMp1(0x36, ref args);
                    if (result == RyzenSmu.SmuStatus.Ok)
                        result = _smu.SendPsmu(0xB, ref args);
                    break;

                case RyzenFamily.VanGogh:
                case RyzenFamily.Rembrandt:
                case RyzenFamily.Phoenix:
                case RyzenFamily.Mendocino:
                case RyzenFamily.HawkPoint:
                case RyzenFamily.StrixPoint:
                    result = _smu.SendPsmu(0x5D, ref args);
                    break;

                case RyzenFamily.StrixHalo:
                    result = _smu.SendMp1(0x4C, ref args);
                    if (result == RyzenSmu.SmuStatus.Ok)
                        result = _smu.SendPsmu(0x5D, ref args);
                    break;

                case RyzenFamily.RaphaelDragonRange:
                case RyzenFamily.FireRange:
                    result = _smu.SendPsmu(0x7, ref args);
                    break;
            }

            return result;
        }

        /// <summary>
        /// Set iGPU Curve Optimizer offset (for APUs).
        /// </summary>
        private RyzenSmu.SmuStatus SetIgpuCO(int value)
        {
            uint uvalue = value < 0
                ? (uint)(0x100000 - (uint)(-value))
                : (uint)value;

            uint[] args = new uint[6];
            args[0] = uvalue;
            RyzenSmu.SmuStatus result = RyzenSmu.SmuStatus.Failed;

            switch (_cpuInfo.Family)
            {
                case RyzenFamily.RenoirLucienne:
                case RyzenFamily.CezanneBarcelo:
                    result = _smu.SendMp1(0x64, ref args);
                    if (result == RyzenSmu.SmuStatus.Ok)
                        result = _smu.SendPsmu(0x57, ref args);
                    break;

                case RyzenFamily.VanGogh:
                case RyzenFamily.Rembrandt:
                case RyzenFamily.Phoenix:
                case RyzenFamily.Mendocino:
                case RyzenFamily.HawkPoint:
                case RyzenFamily.StrixPoint:
                case RyzenFamily.StrixHalo:
                    result = _smu.SendPsmu(0xB7, ref args);
                    break;
            }

            return result;
        }

        /// <summary>
        /// Set STAPM (sustained power) limit in mW.
        /// </summary>
        public RyzenSmu.SmuStatus SetStapmLimit(uint valueMw)
        {
            uint[] args = new uint[6];
            args[0] = valueMw;
            RyzenSmu.SmuStatus result = RyzenSmu.SmuStatus.Failed;

            switch (_cpuInfo.Family)
            {
                case RyzenFamily.Raven:
                case RyzenFamily.Picasso:
                case RyzenFamily.Dali:
                    result = _smu.SendMp1(0x1A, ref args);
                    break;

                case RyzenFamily.RenoirLucienne:
                case RyzenFamily.VanGogh:
                case RyzenFamily.CezanneBarcelo:
                case RyzenFamily.Rembrandt:
                case RyzenFamily.Phoenix:
                case RyzenFamily.Mendocino:
                case RyzenFamily.HawkPoint:
                case RyzenFamily.StrixPoint:
                    result = _smu.SendMp1(0x14, ref args);
                    if (result == RyzenSmu.SmuStatus.Ok)
                        result = _smu.SendPsmu(0x31, ref args);
                    break;
            }

            return result;
        }

        /// <summary>
        /// Set temperature limit in degrees Celsius.
        /// </summary>
        public RyzenSmu.SmuStatus SetTctlTemp(uint tempC)
        {
            uint[] args = new uint[6];
            args[0] = tempC;
            RyzenSmu.SmuStatus result = RyzenSmu.SmuStatus.Failed;

            switch (_cpuInfo.Family)
            {
                case RyzenFamily.Zen1Plus:
                    result = _smu.SendPsmu(0x68, ref args);
                    break;

                case RyzenFamily.Raven:
                case RyzenFamily.Picasso:
                case RyzenFamily.Dali:
                    result = _smu.SendMp1(0x1F, ref args);
                    break;

                case RyzenFamily.RenoirLucienne:
                case RyzenFamily.VanGogh:
                case RyzenFamily.CezanneBarcelo:
                case RyzenFamily.Rembrandt:
                case RyzenFamily.Phoenix:
                case RyzenFamily.Mendocino:
                case RyzenFamily.HawkPoint:
                case RyzenFamily.StrixPoint:
                case RyzenFamily.StrixHalo:
                    result = _smu.SendMp1(0x19, ref args);
                    break;

                case RyzenFamily.Matisse:
                case RyzenFamily.Vermeer:
                    result = _smu.SendMp1(0x23, ref args);
                    if (result == RyzenSmu.SmuStatus.Ok)
                        result = _smu.SendPsmu(0x56, ref args);
                    break;

                case RyzenFamily.RaphaelDragonRange:
                case RyzenFamily.FireRange:
                    result = _smu.SendMp1(0x3F, ref args);
                    if (result == RyzenSmu.SmuStatus.Ok)
                        result = _smu.SendPsmu(0x59, ref args);
                    break;
            }

            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _smu.Dispose();
        }
    }
}
