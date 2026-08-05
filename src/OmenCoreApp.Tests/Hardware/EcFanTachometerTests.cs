using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Board 8D87 reported 0 RPM in the UI while its fans turned at 2760. The V2 command
    /// GetFanRpmDirect (0x38) is refused there, leaving GetFanLevel x 100 as the only RPM
    /// source - and that is the *commanded* level echoed back, which reads 0 under BIOS
    /// automatic control because nothing has been commanded.
    ///
    /// These pin the EC tachometer path that fixes it: that it is preferred over the level
    /// estimate rather than used only as a last resort, that it decodes 16-bit little-endian,
    /// that a genuine zero survives as a zero, and that a model without configured offsets
    /// behaves exactly as before.
    /// </summary>
    public class EcFanTachometerTests
    {
        /// <summary>EC stub serving a fixed byte map; records every address read.</summary>
        private sealed class MapEcAccess : IEcAccess
        {
            private readonly Dictionary<ushort, byte> _bytes;

            public MapEcAccess(Dictionary<ushort, byte> bytes) => _bytes = bytes;

            public List<ushort> ReadAddresses { get; } = new();
            public bool IsAvailable { get; init; } = true;
            public bool Initialize(string devicePath) => true;

            public byte ReadByte(ushort address)
            {
                ReadAddresses.Add(address);
                return _bytes.TryGetValue(address, out var b) ? b : (byte)0x00;
            }

            public void WriteByte(ushort address, byte value) { }
            public void Dispose() { }
        }

        /// <summary>
        /// EC stub driven by a script of per-tick outcomes: a null entry throws the way a busy
        /// ACPI EC port pair does, a tuple serves that fan pair. The last entry repeats once the
        /// script runs out, so "fails once then works" and "never works" are both expressible.
        /// </summary>
        private sealed class ScriptedEcAccess : IEcAccess
        {
            private readonly Queue<(int fan1, int fan2)?> _script;
            private (int fan1, int fan2)? _current;

            public ScriptedEcAccess(params (int fan1, int fan2)?[] ticks)
                => _script = new Queue<(int fan1, int fan2)?>(ticks);

            public List<ushort> ReadAddresses { get; } = new();
            public bool IsAvailable => true;
            public bool Initialize(string devicePath) => true;

            public byte ReadByte(ushort address)
            {
                ReadAddresses.Add(address);

                // A tick begins at the first tachometer's low byte.
                if (address == 0x70 && _script.Count > 0)
                {
                    _current = _script.Dequeue();
                }

                if (_current is null)
                {
                    throw new TimeoutException("EC output buffer not full");
                }

                var rpm = address is 0x70 or 0x71 ? _current.Value.fan1 : _current.Value.fan2;
                return address is 0x71 or 0x5D ? (byte)(rpm >> 8) : (byte)(rpm & 0xFF);
            }

            public void WriteByte(ushort address, byte value) { }
            public void Dispose() { }
        }

        /// <summary>
        /// V1 board: GetFanRpmDirect refused, GetFanLevel answers. Mirrors 8D87, where the
        /// level is the thing that must NOT win over a tachometer.
        /// </summary>
        private sealed class V1FanLevelBios : IHpWmiBios
        {
            public (byte fan1, byte fan2)? Level { get; init; } = (20, 22);

            public bool IsAvailable => true;
            public string Status => "fake V1";
            public HpWmiBios.ThermalPolicyVersion ThermalPolicy => HpWmiBios.ThermalPolicyVersion.V1;
            public int FanCount => 2;
            public int MaxFanLevel => 60;

            // The board this models refuses 0x38 outright - that refusal is the whole reason
            // the level estimate was ever load-bearing.
            public (int fan1Rpm, int fan2Rpm)? GetFanRpmDirect() => null;
            public (byte fan1, byte fan2)? GetFanLevel() => Level;

            public bool SetFanMax(bool enabled) => true;
            public bool SetFanLevel(byte fan1, byte fan2) => true;
            public bool SetFanMode(HpWmiBios.FanMode mode) => true;

            public double? GetTemperature() => 50;
            public double? GetGpuTemperature() => 50;
            public void ExtendFanCountdown() { }

            public (bool customTgp, bool ppab, int dState)? GetGpuPower() => null;
            public bool SetGpuPower(HpWmiBios.GpuPowerLevel level) => true;
            public HpWmiBios.GpuMode? GetGpuMode() => null;

            public void Dispose() { }
        }

        private static WmiFanController CreateController(
            IEcAccess? ecAccess,
            ushort[]? offsets,
            IHpWmiBios? bios = null)
            => new(
                hwMonitor: null,
                logging: null,
                injectedWmiBios: bios ?? new V1FanLevelBios(),
                ecAccess: ecAccess,
                ecFanTachometerOffsets: offsets);

        private static List<FanTelemetry> Read(WmiFanController controller)
            => controller.ReadFanSpeeds().ToList();

        [Fact]
        public void Tachometers_AreReadAs16BitLittleEndian()
        {
            // 0x70/0x71 = C8 0A -> 2760; 0x5C/0x5D = AC 08 -> 2220. Taking only the low byte
            // would give 200 and 172 - the exact failure a sibling probe script hit.
            var ec = new MapEcAccess(new Dictionary<ushort, byte>
            {
                [0x70] = 0xC8, [0x71] = 0x0A,
                [0x5C] = 0xAC, [0x5D] = 0x08
            });

            using var controller = CreateController(ec, new ushort[] { 0x70, 0x5C });
            var fans = Read(controller);

            fans.Should().HaveCount(2);
            fans[0].SpeedRpm.Should().Be(2760);
            fans[1].SpeedRpm.Should().Be(2220);
        }

        [Fact]
        public void Tachometer_BeatsTheFanLevelEstimate()
        {
            // The level says 20 -> 2000 RPM. The tachometer says 2760. Preferring the level
            // would replace a measurement with an echo of the last command.
            var ec = new MapEcAccess(new Dictionary<ushort, byte>
            {
                [0x70] = 0xC8, [0x71] = 0x0A,
                [0x5C] = 0xC8, [0x5D] = 0x0A
            });

            using var controller = CreateController(
                ec, new ushort[] { 0x70, 0x5C }, new V1FanLevelBios { Level = (20, 20) });

            var fans = Read(controller);

            fans[0].SpeedRpm.Should().Be(2760, "the physical tachometer must win over level x 100");
            fans[0].SpeedRpm.Should().NotBe(2000, "2000 is the commanded level echoed back, not a measurement");
            fans[0].RpmSource.Should().Be(RpmSource.EcDirect);
        }

        [Fact]
        public void StoppedFans_ReportZero_RatherThanFallingBackToTheLevel()
        {
            // The whole point of having a tachometer: when it says the fans are stopped, that
            // is the answer, even though the firmware still remembers a non-zero level. Falling
            // through to the level here would resurrect exactly the reading that made a fan
            // change verifiable against its own request.
            var ec = new MapEcAccess(new Dictionary<ushort, byte>
            {
                [0x70] = 0x00, [0x71] = 0x00,
                [0x5C] = 0x00, [0x5D] = 0x00
            });

            using var controller = CreateController(
                ec, new ushort[] { 0x70, 0x5C }, new V1FanLevelBios { Level = (35, 35) });

            var fans = Read(controller);

            fans[0].SpeedRpm.Should().Be(0);
            fans[0].RpmSource.Should().Be(RpmSource.EcDirect, "a measured zero is still a measurement");
            fans[0].SpeedRpm.Should().NotBe(3500, "3500 would be the level estimate leaking back in");
        }

        [Fact]
        public void ImplausibleReading_IsRejected_AndFallsBackToWmi()
        {
            // A mis-aimed offset usually reads as something absurd rather than as an error.
            // 0xFFFF is 65535 RPM; publishing it would be worse than falling back.
            var ec = new MapEcAccess(new Dictionary<ushort, byte>
            {
                [0x70] = 0xFF, [0x71] = 0xFF,
                [0x5C] = 0xFF, [0x5D] = 0xFF
            });

            using var controller = CreateController(
                ec, new ushort[] { 0x70, 0x5C }, new V1FanLevelBios { Level = (20, 22) });

            var fans = Read(controller);

            fans[0].SpeedRpm.Should().Be(2000, "rejecting the EC read must fall back to the level estimate");
            fans[0].RpmSource.Should().Be(RpmSource.Estimated, "and must be labelled as the estimate it is");
        }

        [Fact]
        public void ModelWithoutConfiguredOffsets_TouchesTheEcNotAtAll()
        {
            // Every board that has not had its tachometers located must behave exactly as
            // before - no new EC transactions on the monitoring cadence.
            var ec = new MapEcAccess(new Dictionary<ushort, byte>
            {
                [0x70] = 0xC8, [0x71] = 0x0A
            });

            using var controller = CreateController(ec, offsets: null);
            var fans = Read(controller);

            ec.ReadAddresses.Should().NotContain(0x70);
            fans[0].SpeedRpm.Should().Be(2000, "unchanged: the WMI fan-level estimate");
            fans[0].RpmSource.Should().Be(RpmSource.Estimated);
        }

        [Fact]
        public void UnavailableEcAccess_FallsBackWithoutThrowing()
        {
            var ec = new MapEcAccess(new Dictionary<ushort, byte>()) { IsAvailable = false };

            using var controller = CreateController(ec, new ushort[] { 0x70, 0x5C });
            var fans = Read(controller);

            ec.ReadAddresses.Should().BeEmpty();
            fans[0].SpeedRpm.Should().Be(2000);
        }

        [Fact]
        public void OneFailedTransaction_DoesNotDisableTheTachometerForTheSession()
        {
            // Measured on 8D87: the app logged a single "EC output buffer not full" 39 seconds
            // after start and, because the first failure was treated as permanent, spent the
            // next eight minutes on the fan-level estimate - including a full verification run,
            // which then scored six passes on evidence that was the commanded level echoed back.
            // The port pair is shared with the firmware's own traffic; one refusal is contention.
            var ec = new ScriptedEcAccess(null, (2760, 2220));

            using var controller = CreateController(ec, new ushort[] { 0x70, 0x5C });

            var duringFailure = Read(controller);
            duringFailure[0].RpmSource.Should().Be(RpmSource.Estimated, "the tick itself has no reading");

            var afterRecovery = Read(controller);
            afterRecovery[0].SpeedRpm.Should().Be(2760);
            afterRecovery[0].RpmSource.Should().Be(RpmSource.EcDirect, "the next tick must try again");
        }

        [Fact]
        public void SustainedFailures_StopHammeringTheEc()
        {
            // The other half of the trade: retrying every tick forever would amplify exactly the
            // contention that caused the failure. After a short run of failures it backs off, so
            // an EC that genuinely will not answer costs almost no traffic.
            var ec = new ScriptedEcAccess(null, null, null, null, null);

            using var controller = CreateController(ec, new ushort[] { 0x70, 0x5C });

            for (var tick = 0; tick < 8; tick++)
            {
                Read(controller);
            }

            ec.ReadAddresses.Count.Should().BeLessThan(8, "the backoff must actually stop the traffic");
            ec.ReadAddresses.Should().OnlyContain(a => a == 0x70, "a tick that throws never reaches the second byte");
        }

        [Fact]
        public void OneImplausibleReading_IsDiscardedAsATornRead_NotAsWrongOffsets()
        {
            // A tachometer is two separate byte transactions, so a single absurd pair can be a
            // read torn across an EC update. Writing the offsets off on the strength of one is
            // the same over-reaction as giving up after one timeout.
            var ec = new ScriptedEcAccess((65535, 65535), (2760, 2220));

            using var controller = CreateController(ec, new ushort[] { 0x70, 0x5C });

            Read(controller)[0].RpmSource.Should().Be(RpmSource.Estimated);
            Read(controller)[0].SpeedRpm.Should().Be(2760, "one bad pair is not proof the offsets are wrong");
        }

        [Fact]
        public void RepeatedImplausibleReadings_WriteOffTheOffsetsForGood()
        {
            // A run of them is proof. Offsets pointed at something that is not a tachometer -
            // 0x34/0x35 on this board's neighbours land in an ASCII serial number - must be
            // abandoned rather than retried, because they will read "fine" forever.
            var ec = new ScriptedEcAccess((65535, 65535), (65535, 65535), (65535, 65535), (2760, 2220));

            using var controller = CreateController(ec, new ushort[] { 0x70, 0x5C });

            for (var tick = 0; tick < 3; tick++)
            {
                Read(controller);
            }

            var afterGivingUp = Read(controller);
            afterGivingUp[0].SpeedRpm.Should().Be(2000, "the offsets are written off; this is the level estimate");
            afterGivingUp[0].RpmSource.Should().Be(RpmSource.Estimated);
        }

        [Fact]
        public void TachometerOffsets_AreIndependentOfEcFanControlSupport()
        {
            // 8D87 has SupportsFanControlEc = false and readable tachometers. The two must not
            // be coupled: one gates writes, the other is a read.
            var caps = ModelCapabilityDatabase.GetCapabilities("8D87");

            caps.SupportsFanControlEc.Should().BeFalse();
            caps.EcFanTachometerOffsets.Should().Equal(new ushort[] { 0x70, 0x5C });
        }
    }
}
