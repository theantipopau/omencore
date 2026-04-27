using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class PowerLimitControllerTests
    {
        private sealed class FakeEcAccess : IEcAccess
        {
            private readonly Dictionary<ushort, byte> _registers = new();

            public bool IsAvailable { get; set; } = true;
            public int WriteCount { get; private set; }

            public bool Initialize(string devicePath) => true;

            public byte ReadByte(ushort address)
            {
                return _registers.TryGetValue(address, out var value) ? value : (byte)0;
            }

            public void WriteByte(ushort address, byte value)
            {
                WriteCount++;
                _registers[address] = value;
            }

            public int ReadWord(ushort lowAddress, ushort highAddress)
            {
                return ReadByte(lowAddress) | (ReadByte(highAddress) << 8);
            }

            public void SeedByte(ushort address, byte value)
            {
                _registers[address] = value;
            }

            public void Dispose()
            {
            }
        }

        [Fact]
        public void ApplyPerformanceLimits_DetailedMode_UsesExplicitCpuBoostLimit_WhenProvided()
        {
            var ec = new FakeEcAccess();
            var controller = new PowerLimitController(ec, useSimplifiedMode: false);
            var mode = new PerformanceMode
            {
                Name = "Performance",
                CpuPowerLimitWatts = 90,
                CpuBoostPowerLimitWatts = 130,
                GpuPowerLimitWatts = 150
            };

            controller.ApplyPerformanceLimits(mode);

            ec.ReadWord(0xC0, 0xC1).Should().Be(90 * 8);
            ec.ReadWord(0xC2, 0xC3).Should().Be(130 * 8,
                "explicit model PL2 must not be replaced by the legacy 1.5x PL1 heuristic");
            ec.ReadWord(0xC4, 0xC5).Should().Be(150 * 8);
        }

        [Fact]
        public async Task VerifyPowerLimitsAsync_ReadsBackOnly_DoesNotApplySecondEcWrite()
        {
            var ec = new FakeEcAccess();
            ec.SeedByte(0xCE, 0x02);
            var controller = new PowerLimitController(ec);
            var logging = new LoggingService();
            var verifier = new PowerVerificationService(controller, ec, logging);
            var mode = new PerformanceMode { Name = "Performance", CpuPowerLimitWatts = 90, GpuPowerLimitWatts = 150 };

            var verified = await verifier.VerifyPowerLimitsAsync(mode);

            verified.Should().BeTrue();
            ec.WriteCount.Should().Be(0,
                "verify-only calls should not re-apply EC power limits after PerformanceModeService has already written them");
        }
    }
}
