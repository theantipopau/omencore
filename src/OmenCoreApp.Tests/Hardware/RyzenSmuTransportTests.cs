using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Pins the AMD SMU transport contract.
    ///
    /// The Curve Optimizer was inert on every AMD board, not because of a wrong SMU message id
    /// but because the transport underneath it could not work at all: <see cref="RyzenSmu"/>
    /// opened a PawnIO handle, never called <c>pawnio_load</c>, and then invoked
    /// <c>ioctl_pci_read_config_dword</c> / <c>ioctl_pci_write_config_dword</c> - ioctl names that
    /// no bundled PawnIO module exports. Both halves had to be wrong for the symptom to be
    /// "silently does nothing", so both are pinned here.
    ///
    /// These are static/structural assertions. They deliberately do NOT try to talk to the SMU:
    /// that needs the signed driver, admin rights and specific silicon, and is verified by
    /// measured outcome instead (see tools/smu-probe).
    /// </summary>
    public class RyzenSmuTransportTests
    {
        /// <summary>
        /// The ioctls the bundled RyzenSMU PawnIO module actually exports, read out of the
        /// module binary in <see cref="ExportedIoctlNames_AreTheOnesRyzenSmuCalls"/>.
        /// </summary>
        private static readonly string[] ExpectedSmuIoctls =
        {
            "ioctl_read_smu_register",
            "ioctl_write_smu_register",
            "ioctl_get_smu_version",
        };

        [Fact]
        public void ExportedIoctlNames_AreTheOnesRyzenSmuCalls()
        {
            var modulePath = FindBundledModule("RyzenSMU.bin");
            if (modulePath == null)
            {
                // The module is a build artifact copied next to the test binary; if a given
                // build layout does not stage it, skip rather than fail spuriously.
                return;
            }

            var strings = ExtractAsciiStrings(File.ReadAllBytes(modulePath), minLength: 5);

            foreach (var ioctl in ExpectedSmuIoctls)
            {
                strings.Should().Contain(ioctl,
                    $"RyzenSmu calls {ioctl}, so the bundled module must export it");
            }

            // The regression itself: these were what the code used to call, and the module has
            // never exported them. If a future module gains them, this test should be revisited
            // rather than the old code path restored.
            strings.Should().NotContain("ioctl_pci_read_config_dword",
                "the pre-fix code called this and no bundled module exports it");
            strings.Should().NotContain("ioctl_pci_write_config_dword",
                "the pre-fix code called this and no bundled module exports it");
        }

        [Fact]
        public void RyzenSmu_DoesNotCall_PciConfigIoctls_ThatNoModuleExports()
        {
            var source = ReadRepositorySource("src/OmenCoreApp/Hardware/RyzenSmu.cs");
            if (source == null) return;

            source.Should().NotContain("ioctl_pci_read_config_dword");
            source.Should().NotContain("ioctl_pci_write_config_dword");
            source.Should().Contain("ioctl_read_smu_register");
            source.Should().Contain("ioctl_write_smu_register");
        }

        [Fact]
        public void RyzenSmu_LoadsAPawnIoModule()
        {
            var source = ReadRepositorySource("src/OmenCoreApp/Hardware/RyzenSmu.cs");
            if (source == null) return;

            // Opening a handle without loading a module leaves every ioctl unbound - the exact
            // shape of the original bug.
            source.Should().Contain("pawnio_load",
                "a PawnIO handle with no module loaded cannot service any ioctl");
            source.Should().Contain("RyzenSMU.bin");
        }

        [Fact]
        public void SmuRegisterAddressGuard_MatchesTheModulesOwnRangeCheck()
        {
            // Mirrors check_smu_register_range in namazso/PawnIO.Modules RyzenSMU.p. Anything
            // outside these windows is rejected by the module, so the guard exists to turn that
            // into a diagnosable message rather than an opaque HRESULT.

            // SMU mailboxes, 0x3B10000-0x3B10FFF. The Strix Point mailboxes live here.
            RyzenSmu.IsSmuRegisterAddressSupported(0x3B10928).Should().BeTrue("MP1 message register");
            RyzenSmu.IsSmuRegisterAddressSupported(0x3B10978).Should().BeTrue("MP1 response register");
            RyzenSmu.IsSmuRegisterAddressSupported(0x3B10998).Should().BeTrue("MP1 argument base");
            RyzenSmu.IsSmuRegisterAddressSupported(0x3B10A20).Should().BeTrue("PSMU message register");
            RyzenSmu.IsSmuRegisterAddressSupported(0x3B10A80).Should().BeTrue("PSMU response register");
            RyzenSmu.IsSmuRegisterAddressSupported(0x3B10A88).Should().BeTrue("PSMU argument base");

            // Boundaries of the mailbox window.
            RyzenSmu.IsSmuRegisterAddressSupported(0x3B10000).Should().BeTrue();
            RyzenSmu.IsSmuRegisterAddressSupported(0x3B10FFF).Should().BeTrue();
            RyzenSmu.IsSmuRegisterAddressSupported(0x3B0FFFF).Should().BeFalse();
            RyzenSmu.IsSmuRegisterAddressSupported(0x3B11000).Should().BeFalse();

            // Pre-Ryzen mailboxes, SVI2 planes, extended SVI2 planes.
            RyzenSmu.IsSmuRegisterAddressSupported(0x13000000).Should().BeTrue();
            RyzenSmu.IsSmuRegisterAddressSupported(0x130000F0).Should().BeTrue();
            RyzenSmu.IsSmuRegisterAddressSupported(0x130000F1).Should().BeFalse();
            RyzenSmu.IsSmuRegisterAddressSupported(0x56000).Should().BeTrue();
            RyzenSmu.IsSmuRegisterAddressSupported(0x5AFFF).Should().BeTrue();
            RyzenSmu.IsSmuRegisterAddressSupported(0x5B000).Should().BeFalse();
            RyzenSmu.IsSmuRegisterAddressSupported(0x6F000).Should().BeTrue();
            RyzenSmu.IsSmuRegisterAddressSupported(0x6FFFF).Should().BeTrue();

            // An unconfigured mailbox (RyzenControl leaves these 0 for unknown families) must
            // not be treated as a valid address.
            RyzenSmu.IsSmuRegisterAddressSupported(0).Should().BeFalse();
        }

        [Fact]
        public void StrixPoint_MailboxAddresses_MatchRyzenAdj()
        {
            // Cross-checked against RyzenAdj lib/nb_smu_ops.h:
            //   MP1_C2PMSG_{MESSAGE,RESPONSE}_ADDR_3 / ARG_BASE_3  (Strix Point uses MP1 set 3)
            //   PSMU_C2PMSG_{MESSAGE,RESPONSE}_ADDR_1 / ARG_BASE_1
            var smu = new RyzenSmu();
            try
            {
                ConfigureFor(smu, RyzenFamily.StrixPoint);

                smu.Mp1AddrMsg.Should().Be(0x3B10928);
                smu.Mp1AddrRsp.Should().Be(0x3B10978);
                smu.Mp1AddrArg.Should().Be(0x3B10998);
                smu.PsmuAddrMsg.Should().Be(0x3B10A20);
                smu.PsmuAddrRsp.Should().Be(0x3B10A80);
                smu.PsmuAddrArg.Should().Be(0x3B10A88);
            }
            finally
            {
                smu.Dispose();
            }
        }

        [Fact]
        public void UnavailableReason_IsPopulated_BeforeInitialize()
        {
            // A user-facing block reason must never be empty while the backend is unavailable,
            // otherwise the UI reports "unavailable" with no explanation.
            var smu = new RyzenSmu();
            try
            {
                smu.IsAvailable.Should().BeFalse();
                smu.UnavailableReason.Should().NotBeNullOrWhiteSpace();
            }
            finally
            {
                smu.Dispose();
            }
        }

        /// <summary>
        /// Strix Point must not be grouped with Phoenix for All-Core Curve Optimizer. RyzenAdj's
        /// set_coall sends MP1 0x4C for FAM_STRIXPOINT, and that is the path measured working on
        /// board 8D87 (+150 MHz sustained at CO -25 against a +0.2% sham control).
        /// </summary>
        [Fact]
        public void StrixPoint_AllCoreCurveOptimizer_UsesMp1_0x4C()
        {
            var source = ReadRepositorySource("src/OmenCoreApp/Hardware/AmdUndervoltProvider.cs");
            if (source == null) return;

            var setAllCoreCo = ExtractMethodBody(source, "private RyzenSmu.SmuStatus SetAllCoreCO");
            setAllCoreCo.Should().NotBeNull();

            var strixPointCase = setAllCoreCo!.IndexOf("case RyzenFamily.StrixPoint:", StringComparison.Ordinal);
            strixPointCase.Should().BeGreaterThan(-1);

            // The statement that runs for Strix Point, up to its break.
            var breakIndex = setAllCoreCo.IndexOf("break;", strixPointCase, StringComparison.Ordinal);
            breakIndex.Should().BeGreaterThan(strixPointCase);
            var strixPointBody = setAllCoreCo.Substring(strixPointCase, breakIndex - strixPointCase);

            strixPointBody.Should().Contain("SendMp1(0x4C",
                "RyzenAdj's set_coall uses MP1 0x4C for FAM_STRIXPOINT");
            strixPointBody.Should().NotContain("0x5D",
                "Strix Point must no longer fall through the Phoenix PSMU 0x5D path");
        }

        private static void ConfigureFor(RyzenSmu smu, RyzenFamily family)
        {
            // RyzenControl reads the live CPU, so drive its family field directly to test the
            // mapping for a specific part. Reflection against private state is the established
            // pattern in this suite where the code under test is not otherwise reachable.
            var familyProperty = typeof(RyzenControl).GetProperty(
                nameof(RyzenControl.Family), BindingFlags.Public | BindingFlags.Static);
            familyProperty.Should().NotBeNull();

            var initialized = typeof(RyzenControl).GetField(
                "_initialized", BindingFlags.NonPublic | BindingFlags.Static);
            initialized.Should().NotBeNull();

            var previousFamily = RyzenControl.Family;
            var previousInitialized = initialized!.GetValue(null);
            try
            {
                initialized.SetValue(null, true);
                familyProperty!.SetValue(null, family);
                RyzenControl.ConfigureSmuAddresses(smu);
            }
            finally
            {
                familyProperty!.SetValue(null, previousFamily);
                initialized.SetValue(null, previousInitialized);
            }
        }

        private static string? ExtractMethodBody(string source, string signatureFragment)
        {
            var start = source.IndexOf(signatureFragment, StringComparison.Ordinal);
            if (start < 0) return null;

            var braceStart = source.IndexOf('{', start);
            if (braceStart < 0) return null;

            var depth = 0;
            for (var i = braceStart; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0) return source.Substring(braceStart, i - braceStart + 1);
                }
            }

            return null;
        }

        private static HashSet<string> ExtractAsciiStrings(byte[] data, int minLength)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            var current = new List<char>();

            foreach (var b in data)
            {
                if (b >= 0x20 && b <= 0x7E)
                {
                    current.Add((char)b);
                }
                else
                {
                    if (current.Count >= minLength) found.Add(new string(current.ToArray()));
                    current.Clear();
                }
            }

            if (current.Count >= minLength) found.Add(new string(current.ToArray()));
            return found;
        }

        private static string? FindBundledModule(string fileName)
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "drivers", fileName),
                Path.Combine(RepositoryRoot() ?? ".", "src", "OmenCoreApp", "drivers", fileName),
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string? ReadRepositorySource(string relativePath)
        {
            var root = RepositoryRoot();
            if (root == null) return null;

            var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full) ? File.ReadAllText(full) : null;
        }

        private static string? RepositoryRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "OmenCore.sln"))) return dir.FullName;
                dir = dir.Parent;
            }

            return null;
        }
    }
}
