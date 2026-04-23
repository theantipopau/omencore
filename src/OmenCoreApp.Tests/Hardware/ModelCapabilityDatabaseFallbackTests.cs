// Area C — Hardware-failure-safe behavior (no-hardware session 2026-04-16)
// Tests for ModelCapabilityDatabase fallback paths (RISK-7 from REGRESSION_MATRIX):
// "GetCapabilities("FFFFFFFF") returns non-null, usable safe defaults."
// No physical OMEN hardware is required for any test in this file.
// All claims here are code-path-only; hardware behavior is NOT validated.

using FluentAssertions;
using OmenCore.Hardware;
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
