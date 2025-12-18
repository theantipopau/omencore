using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OmenCore.Services.SystemOptimizer.Optimizations
{
    /// <summary>
    /// Handles network optimizations: TCP settings, Nagle algorithm, Delivery Optimization.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class NetworkOptimizer
    {
        private readonly LoggingService _logger;
        private readonly RegistryBackupService _backup;

        public NetworkOptimizer(LoggingService logger, RegistryBackupService backup)
        {
            _logger = logger;
            _backup = backup;
        }

        public async Task<NetworkOptimizationState> GetStateAsync()
        {
            return await Task.Run(() => new NetworkOptimizationState
            {
                TcpNoDelay = IsTcpNoDelayEnabled(),
                TcpAckFrequency = IsTcpAckFrequencyOptimized(),
                DeliveryOptimizationDisabled = IsDeliveryOptimizationDisabled(),
                NagleDisabled = IsTcpNoDelayEnabled() // They're typically set together
            });
        }

        public async Task<List<OptimizationResult>> ApplyAllAsync()
        {
            var results = new List<OptimizationResult>();
            
            results.Add(await ApplyTcpNoDelayAsync());
            results.Add(await ApplyTcpAckFrequencyAsync());
            results.Add(await DisableDeliveryOptimizationAsync());
            results.Add(await DisableNagleAsync());
            
            return results;
        }

        public async Task<List<OptimizationResult>> ApplyRecommendedAsync()
        {
            var results = new List<OptimizationResult>();
            
            // Recommended: TCP optimizations, Delivery Optimization disabled
            results.Add(await ApplyTcpNoDelayAsync());
            results.Add(await ApplyTcpAckFrequencyAsync());
            results.Add(await DisableDeliveryOptimizationAsync());
            
            return results;
        }

        public async Task<List<OptimizationResult>> RevertAllAsync()
        {
            var results = new List<OptimizationResult>();
            
            results.Add(await RevertTcpNoDelayAsync());
            results.Add(await RevertTcpAckFrequencyAsync());
            results.Add(await EnableDeliveryOptimizationAsync());
            results.Add(await RevertNagleAsync());
            
            return results;
        }

        public async Task<OptimizationResult> ApplyAsync(string optimizationId)
        {
            return optimizationId switch
            {
                "network_tcp_nodelay" => await ApplyTcpNoDelayAsync(),
                "network_tcp_ack" => await ApplyTcpAckFrequencyAsync(),
                "network_delivery_opt" => await DisableDeliveryOptimizationAsync(),
                "network_nagle" => await DisableNagleAsync(),
                _ => new OptimizationResult { Id = optimizationId, Success = false, ErrorMessage = "Unknown optimization" }
            };
        }

        public async Task<OptimizationResult> RevertAsync(string optimizationId)
        {
            return optimizationId switch
            {
                "network_tcp_nodelay" => await RevertTcpNoDelayAsync(),
                "network_tcp_ack" => await RevertTcpAckFrequencyAsync(),
                "network_delivery_opt" => await EnableDeliveryOptimizationAsync(),
                "network_nagle" => await RevertNagleAsync(),
                _ => new OptimizationResult { Id = optimizationId, Success = false, ErrorMessage = "Unknown optimization" }
            };
        }

        // ========== TCP NO DELAY ==========

        private async Task<OptimizationResult> ApplyTcpNoDelayAsync()
        {
            var result = new OptimizationResult
            {
                Id = "network_tcp_nodelay",
                Name = "TCP No Delay",
                Category = "Network"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.SetRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                        "TcpNoDelay",
                        1,
                        RegistryValueKind.DWord);
                    result.Success = true;
                });
                
                _logger.Info("TCP NoDelay enabled");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> RevertTcpNoDelayAsync()
        {
            var result = new OptimizationResult
            {
                Id = "network_tcp_nodelay",
                Name = "TCP No Delay",
                Category = "Network"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.RestoreRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                        "TcpNoDelay");
                    result.Success = true;
                });
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ========== TCP ACK FREQUENCY ==========

        private async Task<OptimizationResult> ApplyTcpAckFrequencyAsync()
        {
            var result = new OptimizationResult
            {
                Id = "network_tcp_ack",
                Name = "TCP ACK Frequency",
                Category = "Network"
            };

            try
            {
                await Task.Run(() =>
                {
                    // TcpAckFrequency = 1 means ACK every packet immediately
                    _backup.SetRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                        "TcpAckFrequency",
                        1,
                        RegistryValueKind.DWord);
                    result.Success = true;
                });
                
                _logger.Info("TCP ACK frequency optimized");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> RevertTcpAckFrequencyAsync()
        {
            var result = new OptimizationResult
            {
                Id = "network_tcp_ack",
                Name = "TCP ACK Frequency",
                Category = "Network"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.RestoreRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                        "TcpAckFrequency");
                    result.Success = true;
                });
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ========== DELIVERY OPTIMIZATION ==========

        private async Task<OptimizationResult> DisableDeliveryOptimizationAsync()
        {
            var result = new OptimizationResult
            {
                Id = "network_delivery_opt",
                Name = "Delivery Optimization (P2P Updates)",
                Category = "Network"
            };

            try
            {
                await Task.Run(() =>
                {
                    // DODownloadMode = 0 means HTTP only (no P2P)
                    _backup.SetRegistryValue(
                        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
                        "DODownloadMode",
                        0,
                        RegistryValueKind.DWord);
                    result.Success = true;
                });
                
                _logger.Info("Delivery Optimization (P2P) disabled");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> EnableDeliveryOptimizationAsync()
        {
            var result = new OptimizationResult
            {
                Id = "network_delivery_opt",
                Name = "Delivery Optimization (P2P Updates)",
                Category = "Network"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.RestoreRegistryValue(
                        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
                        "DODownloadMode");
                    result.Success = true;
                });
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ========== NAGLE ALGORITHM ==========

        private async Task<OptimizationResult> DisableNagleAsync()
        {
            var result = new OptimizationResult
            {
                Id = "network_nagle",
                Name = "Nagle Algorithm",
                Category = "Network"
            };

            try
            {
                await Task.Run(() =>
                {
                    // This needs to be set per-interface, so we set a global hint
                    // and the TCP NoDelay already helps disable Nagle behavior
                    _backup.SetRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                        "TCPDelAckTicks",
                        0,
                        RegistryValueKind.DWord);
                    result.Success = true;
                });
                
                _logger.Info("Nagle algorithm optimization applied");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<OptimizationResult> RevertNagleAsync()
        {
            var result = new OptimizationResult
            {
                Id = "network_nagle",
                Name = "Nagle Algorithm",
                Category = "Network"
            };

            try
            {
                await Task.Run(() =>
                {
                    _backup.RestoreRegistryValue(
                        @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                        "TCPDelAckTicks");
                    result.Success = true;
                });
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ========== STATE CHECKS ==========

        private bool IsTcpNoDelayEnabled()
        {
            var value = _backup.GetRegistryValue(
                @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                "TcpNoDelay");
            return value != null && (int)value == 1;
        }

        private bool IsTcpAckFrequencyOptimized()
        {
            var value = _backup.GetRegistryValue(
                @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                "TcpAckFrequency");
            return value != null && (int)value == 1;
        }

        private bool IsDeliveryOptimizationDisabled()
        {
            var value = _backup.GetRegistryValue(
                @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
                "DODownloadMode");
            return value != null && (int)value == 0;
        }
    }
}
