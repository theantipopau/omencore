// Area C — Hardware-failure-safe behavior (no-hardware session 2026-04-16)
// Tests for ModelCapabilityDatabase fallback paths (RISK-7 from REGRESSION_MATRIX):
// "GetCapabilities("FFFFFFFF") returns non-null, usable safe defaults."
// No physical OMEN hardware is required for any test in this file.
// All claims here are code-path-only; hardware behavior is NOT validated.

using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class ModelCapabilityDatabaseFallbackTests
    {
        // ─── Unknown product IDs return safe non-null defaults ────────────────

        [Fact]
        public void GetCapabilities_UnknownProductId_ReturnsNonNullDefault()
        {
            // T3 from REGRESSION_MATRIX: unknown productId must never return null.
            // A null return would crash callers that dereference SupportsFanControlWmi etc.
            var caps = ModelCapabilityDatabase.GetCapabilities("FFFFFFFF");

            caps.Should().NotBeNull("unknown productId must fall back to DefaultCapabilities, never null");
        }

        [Fact]
        public void GetCapabilities_UnknownProductId_HasSafeDefaults()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("FFFFFFFF");

            // Default entry must allow WMI fan control so the app can still operate
            caps.SupportsFanControlWmi.Should().BeTrue(
                "default capabilities must enable WMI fan control as a safe fallback");
            caps.FanZoneCount.Should().BeGreaterThan(0,
                "default capabilities must specify at least one fan zone");
        }

        // ─── GitHub #182: DefaultCapabilities must not claim write-capable features ───

        [Fact]
        public void DefaultCapabilities_DoesNotClaimWriteCapableFeaturesAsSupported()
        {
            // GitHub #182 (board 8603, OMEN 17-cb0xxx): a completely unrecognized board used to
            // inherit SupportsFanCurves/SupportsGpuPowerBoost/HasFourZoneRgb = true from this
            // fallback "so the app can still operate" - but those are hardware-specific,
            // write-capable claims, not baseline operability. The reporter's own OmenMon probe
            // showed GetGpuPower() failing outright on that exact hardware, directly
            // contradicting the "Supported" badge this fallback was producing.
            var caps = ModelCapabilityDatabase.DefaultCapabilities;

            caps.SupportsFanControlEc.Should().BeFalse("EC fan control must not be assumed on a completely unrecognized board");
            caps.SupportsFanCurves.Should().BeFalse("custom fan curves must not be assumed on a completely unrecognized board");
            caps.SupportsIndependentFanCurves.Should().BeFalse("independent fan curves must not be assumed on a completely unrecognized board");
            caps.SupportsGpuPowerBoost.Should().BeFalse("GPU Power Boost must not be assumed on a completely unrecognized board");
            caps.SupportsUndervolt.Should().BeFalse("undervolt must not be assumed on a completely unrecognized board");
            caps.SupportsTccOffset.Should().BeFalse("TCC offset must not be assumed on a completely unrecognized board");
            caps.SupportsPowerLimits.Should().BeFalse("direct power limits must not be assumed on a completely unrecognized board");
            caps.HasFourZoneRgb.Should().BeFalse("4-zone RGB must not be assumed on a completely unrecognized board");

            // Basic WMI fan-mode switching and OEM performance profiles are the one thing safe
            // to assume for any HP OMEN/Victus laptop - the app must still be able to operate.
            caps.SupportsFanControlWmi.Should().BeTrue("WMI BIOS fan-mode switching is the one thing safe to assume for any HP OMEN/Victus laptop");
            caps.SupportsPerformanceModes.Should().BeTrue("OEM performance profiles are the one thing safe to assume for any HP OMEN/Victus laptop");
        }

        [Fact]
        public void GetCapabilitiesByFamily_DoesNotInheritWriteCapableFeaturesFromTemplateBoard()
        {
            // GitHub #182: this used to clone essentially every feature flag from "the first
            // model of this family in dictionary order" - so whichever board happened to be
            // enumerated first decided what every other, unrelated, unverified board in that
            // family claimed to support. Assert it no longer does, across every family that
            // actually has a template board in the database.
            foreach (OmenModelFamily family in System.Enum.GetValues<OmenModelFamily>())
            {
                var caps = ModelCapabilityDatabase.GetCapabilitiesByFamily(family);
                if (caps.ProductId == "DEFAULT")
                {
                    // No template board for this family - falls through to DefaultCapabilities,
                    // already covered by the test above.
                    continue;
                }

                caps.SupportsFanControlEc.Should().BeFalse($"{family}: EC fan control must not be inherited from an arbitrary template board");
                caps.SupportsFanCurves.Should().BeFalse($"{family}: fan curves must not be inherited from an arbitrary template board");
                caps.SupportsIndependentFanCurves.Should().BeFalse($"{family}: independent fan curves must not be inherited from an arbitrary template board");
                caps.HasMuxSwitch.Should().BeFalse($"{family}: MUX switch presence must not be inherited from an arbitrary template board");
                caps.SupportsGpuPowerBoost.Should().BeFalse($"{family}: GPU Power Boost must not be inherited from an arbitrary template board");
                caps.HasFourZoneRgb.Should().BeFalse($"{family}: 4-zone RGB must not be inherited from an arbitrary template board");
                caps.HasPerKeyRgb.Should().BeFalse($"{family}: per-key RGB must not be inherited from an arbitrary template board");
                caps.SupportsUndervolt.Should().BeFalse($"{family}: undervolt must not be inherited from an arbitrary template board");
                caps.SupportsTccOffset.Should().BeFalse($"{family}: TCC offset must not be inherited from an arbitrary template board");
                caps.SupportsPowerLimits.Should().BeFalse($"{family}: direct power limits must not be inherited from an arbitrary template board");
                caps.UserVerified.Should().BeFalse($"{family}: a family-fallback match is never user-verified");
            }
        }

        [Fact]
        public void GetCapabilities_EmptyString_ReturnsNonNullDefault()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities(string.Empty);

            caps.Should().NotBeNull("empty productId must fall back to DefaultCapabilities");
        }

        [Fact]
        public void GetCapabilities_NullString_ReturnsNonNullDefault()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities(null!);

            caps.Should().NotBeNull("null productId must fall back to DefaultCapabilities");
        }

        [Fact]
        public void GetCapabilities_CaseInsensitive_ReturnsKnownModel()
        {
            // ProductId lookup must be case-insensitive (database stores upper-case keys).
            var lower = ModelCapabilityDatabase.GetCapabilities("8a14");
            var upper = ModelCapabilityDatabase.GetCapabilities("8A14");

            lower.Should().NotBeNull();
            upper.Should().NotBeNull();
            lower!.ProductId.Should().Be(upper!.ProductId,
                "productId lookup must be case-insensitive");
        }

        // ─── DefaultCapabilities property is never null ───────────────────────

        [Fact]
        public void DefaultCapabilities_IsNotNull()
        {
            ModelCapabilityDatabase.DefaultCapabilities.Should().NotBeNull();
        }

        [Fact]
        public void DefaultCapabilities_ProductId_IsDefault()
        {
            ModelCapabilityDatabase.DefaultCapabilities.ProductId.Should().Be("DEFAULT");
        }

        // ─── GetCapabilitiesByModelName: unknown model returns null (caller must handle) ─

        [Fact]
        public void GetCapabilitiesByModelName_UnknownModel_ReturnsNull()
        {
            // Callers must guard against null from GetCapabilitiesByModelName.
            // This test verifies the contract: unknown model → null (caller falls back to GetCapabilitiesByFamily or DefaultCapabilities).
            var caps = ModelCapabilityDatabase.GetCapabilitiesByModelName("Some Unknown Laptop XYZ 9999");

            caps.Should().BeNull(
                "unknown WMI model names must return null so callers can apply their own fallback strategy");
        }

        [Fact]
        public void GetCapabilitiesByModelName_EmptyString_ReturnsNull()
        {
            var caps = ModelCapabilityDatabase.GetCapabilitiesByModelName(string.Empty);

            caps.Should().BeNull("empty model name must return null per method contract");
        }

        [Fact]
        public void GetCapabilitiesByModelName_KnownPattern_ReturnsNonNull()
        {
            // The OMEN MAX 16 ak0003nr entry has ModelNamePattern "max 16 ak0".
            var caps = ModelCapabilityDatabase.GetCapabilitiesByModelName("OMEN MAX 16 ak0003nr");

            caps.Should().NotBeNull("known WMI model name pattern must resolve to a database entry");
        }

        // ─── GetCapabilitiesByModelName: vendor-restricted entries (GitHub #172) ──────

        [Fact]
        public void GetCapabilitiesByModelName_MatchingVendor_ReturnsRestrictedEntry()
        {
            // The 8C2F entry (ModelNamePattern "16-r0") is RequiredCpuVendor = AMD.
            var caps = ModelCapabilityDatabase.GetCapabilitiesByModelName(
                "Victus by HP Gaming Laptop 16-r0xxx", CpuUndervoltProviderFactory.CpuVendor.AMD);

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8C2F");
        }

        [Fact]
        public void GetCapabilitiesByModelName_MismatchedVendor_DoesNotReturnRestrictedEntry()
        {
            // GitHub #172 (then): board 8BBE reported the same WMI name pattern as the AMD-only
            // 8C2F entry ("Victus by HP Gaming Laptop 16-r0xxx") but is an Intel system, and had
            // no entry of its own - so an Intel machine reaching this name pattern had to resolve
            // to null (Family fallback), never to 8C2F's AMD-derived flags (e.g.
            // SupportsUndervolt = false, which assumes Ryzen). GitHub #211 (now): 8BBE has its own
            // exact, Intel-gated entry, so the SAME lookup correctly matches IT instead of null -
            // the guard is proven by checking it lands on the Intel entry, not the AMD one.
            var caps = ModelCapabilityDatabase.GetCapabilitiesByModelName(
                "Victus by HP Gaming Laptop 16-r0xxx", CpuUndervoltProviderFactory.CpuVendor.Intel);

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8BBE");
            caps.RequiredCpuVendor.Should().Be(CpuUndervoltProviderFactory.CpuVendor.Intel);
        }

        [Fact]
        public void GetPreferredCapabilities_8E9F_MaxAk1_IsExactAndConservative()
        {
            // GitHub #224: HyperX OMEN MAX 16-ak1xxx previously fell to the unverified OMEN16 family default.
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities(
                "8E9F", "HyperX OMEN MAX Gaming Laptop 16-ak1xxx", CpuUndervoltProviderFactory.CpuVendor.AMD);

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8E9F");
            caps.FanZoneCount.Should().Be(2);
            caps.SupportsGpuPowerBoost.Should().BeTrue();
            caps.SupportsFanCurves.Should().BeFalse("curves are unconfirmed on this board");
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetPreferredCapabilities_8A3C_IsExact_TwoFans_AndLeaves8A3ENameMatchAlone()
        {
            // GitHub #271: 8A3C used to borrow 8A3E's 1-fan entry through the shared "15-fb0" name.
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities(
                "8A3C", "Victus by HP Gaming Laptop 15-fb0xxx", CpuUndervoltProviderFactory.CpuVendor.AMD);

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8A3C");
            caps.FanZoneCount.Should().Be(2);
            caps.MaxFanLevel.Should().Be(55);
            caps.SupportsFanCurves.Should().BeTrue();
            caps.SupportsIndependentFanCurves.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();

            // An unlisted 15-fb0 board still resolves by name to 8A3E, as before.
            ModelCapabilityDatabase.GetPreferredCapabilities(
                    "FFF0", "Victus by HP Gaming Laptop 15-fb0xxx", CpuUndervoltProviderFactory.CpuVendor.AMD)!
                .ProductId.Should().Be("8A3E");
        }

        [Fact]
        public void GetPreferredCapabilities_88EB_IsExact_AndLeaves88ECNameMatchAlone()
        {
            // GitHub #273: 88EB used to borrow 88EC through the shared "16-e0" name and passed 6/6 on that profile.
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities(
                "88EB", "Victus by HP Laptop 16-e0xxx", CpuUndervoltProviderFactory.CpuVendor.AMD);

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("88EB");
            caps.FanZoneCount.Should().Be(2);
            caps.MaxFanLevel.Should().Be(55);
            caps.SupportsFanCurves.Should().BeTrue();
            caps.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue();
            caps.UserVerified.Should().BeFalse();

            ModelCapabilityDatabase.GetPreferredCapabilities(
                    "FFF0", "Victus by HP Laptop 16-e0xxx", CpuUndervoltProviderFactory.CpuVendor.AMD)!
                .ProductId.Should().Be("88EC");
        }

        [Fact]
        public void AutomaticFanCurve_IsOnlyOfferedOn8D87_AndOffByDefaultInConfig()
        {
            ModelCapabilityDatabase.GetPreferredCapabilities(
                    "8D87", "OMEN MAX Gaming Laptop 16-ak0xxx", CpuUndervoltProviderFactory.CpuVendor.AMD)!
                .SupportsAutomaticFanCurve.Should().BeTrue();

            foreach (var other in new[] { "8E9F", "8A13", "8EEC", "8BD4" })
            {
                ModelCapabilityDatabase.GetPreferredCapabilities(other, "", CpuUndervoltProviderFactory.CpuVendor.AMD)
                    ?.SupportsAutomaticFanCurve.Should().NotBe(true, other);
            }

            new FeaturePreferences().AutomaticFanCurveEnabled.Should().BeFalse();
        }

        [Fact]
        public void GetPreferredCapabilities_8E9A_MaxAh100_IsExactAndConservative()
        {
            // GitHub #142: HyperX OMEN MAX 16t-ah100 previously fell to the unverified OMEN16 family default.
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities(
                "8E9A", "HyperX OMEN MAX Gaming Laptop 16t-ah100", CpuUndervoltProviderFactory.CpuVendor.Intel);

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8E9A");
            caps.FanZoneCount.Should().Be(3, "the firmware reported three fans in the export");
            caps.SupportsGpuPowerBoost.Should().BeTrue();
            caps.SupportsUndervolt.Should().BeFalse("the PawnIO MSR write failed on this board");
            caps.SupportsFanCurves.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetPreferredCapabilities_8A13_Omen16B1_IsExactAndConservative()
        {
            // GitHub #225: OMEN 16-b1xxx previously fell to the unverified OMEN16 family default.
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities(
                "8A13", "OMEN by HP Laptop 16-b1xxx", CpuUndervoltProviderFactory.CpuVendor.Intel);

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8A13");
            caps.FanZoneCount.Should().Be(2);
            caps.SupportsGpuPowerBoost.Should().BeTrue();
            caps.HasFourZoneRgb.Should().BeTrue();
            caps.SupportsFanCurves.Should().BeFalse("curves are unconfirmed on this board");
            caps.SupportsUndervolt.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetPreferredCapabilities_8EEC_HyperxOmen15_IsExactAndConservative()
        {
            // GitHub #223: HyperX OMEN 15-gb0xxx previously fell to the unverified family default.
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities(
                "8EEC", "HyperX OMEN Gaming Laptop 15-gb0xxx", CpuUndervoltProviderFactory.CpuVendor.AMD);

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8EEC");
            caps.FanZoneCount.Should().Be(2);
            caps.SupportsFanCurves.Should().BeFalse();
            caps.SupportsUndervolt.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetPreferredCapabilities_MismatchedVendor_DoesNotCrossVendorViaNamePattern()
        {
            // Same scenario via the full resolution path used by CapabilityDetectionService.
            // ProductId 8BBE now has its own entry (GitHub #211), so this is an exact match, not
            // a name-pattern fallback - stronger evidence than before, same non-null outcome.
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities(
                "8BBE", "Victus by HP Gaming Laptop 16-r0xxx", CpuUndervoltProviderFactory.CpuVendor.Intel);

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8BBE");
        }

        [Fact]
        public void GetCapabilitiesByModelName_AmdOnTheSameNamePattern_DoesNotCrossVendorIntoTheNewIntelEntry()
        {
            // The reverse direction #211 introduces: 8BBE (Intel) and 8C2F (AMD) now share the
            // "16-r0" name pattern from opposite vendor guards. An AMD machine with an unknown
            // ProductId reaching this name pattern must still land on 8C2F, never on 8BBE.
            var caps = ModelCapabilityDatabase.GetCapabilitiesByModelName(
                "Victus by HP Gaming Laptop 16-r0xxx", CpuUndervoltProviderFactory.CpuVendor.AMD);

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8C2F");
        }

        // ─── GetCapabilitiesByFamily: always returns non-null ─────────────────

        [Fact]
        public void GetCapabilitiesByFamily_Unknown_ReturnsNonNull()
        {
            var caps = ModelCapabilityDatabase.GetCapabilitiesByFamily(OmenModelFamily.Unknown);

            caps.Should().NotBeNull(
                "GetCapabilitiesByFamily must always return a usable object, even for Unknown family");
        }

        [Fact]
        public void GetCapabilitiesByFamily_AllFamilies_ReturnNonNull()
        {
            foreach (OmenModelFamily family in System.Enum.GetValues<OmenModelFamily>())
            {
                var caps = ModelCapabilityDatabase.GetCapabilitiesByFamily(family);

                caps.Should().NotBeNull($"GetCapabilitiesByFamily({family}) must never return null");
            }
        }

        // ─── GetAllModels: database is populated ─────────────────────────────

        [Fact]
        public void GetAllModels_ReturnsAtLeastTenEntries()
        {
            var models = ModelCapabilityDatabase.GetAllModels();

            models.Count.Should().BeGreaterThanOrEqualTo(10,
                "database must contain at least 10 known models; if this fails the database was cleared accidentally");
        }

        [Fact]
        public void IsKnownModel_KnownProductId_ReturnsTrue()
        {
            ModelCapabilityDatabase.IsKnownModel("8A14").Should().BeTrue();
        }

        [Fact]
        public void IsKnownModel_UnknownProductId_ReturnsFalse()
        {
            ModelCapabilityDatabase.IsKnownModel("FFFFFFFF").Should().BeFalse();
        }
    }
}
