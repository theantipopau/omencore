using System;
using System.Windows.Media;
using FluentAssertions;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    /// <summary>
    /// A clean build proves the embedded Roboto Condensed resource compiles into the assembly; it
    /// does not prove WPF can actually render text with it — that's a separate, real failure mode
    /// only visible at runtime. These tests check what they reliably can without ever launching the
    /// full hardware-control app (which this project's standing rule forbids doing in this
    /// environment) — FontFamily resolution needs no WPF Application or Dispatcher, just the
    /// assembly's own embedded resources, so it's safe to run in the headless test host.
    ///
    /// Investigation note, recorded honestly rather than papered over: an earlier version of this
    /// file also asserted on GlyphTypeface resolution and the exact set of weights WPF exposes for
    /// the embedded variable font (Roboto Condensed ships as a single OpenType variable font — wght
    /// axis, glyf+gvar outlines — the only format Google's official distribution provides). That
    /// behavior turned out to be genuinely non-deterministic in this test host: run in isolation,
    /// glyph resolution failed and only 3 of 9 weights appeared; run as part of the full suite, all
    /// 9 weights appeared and glyph resolution succeeded. Two reasonable warm-up hypotheses
    /// (touching Fonts.SystemFontFamilies; forcing a FormattedText layout with a plain system font)
    /// were tried and neither reproduced the full-suite result deterministically. Rather than ship
    /// a test that asserts a direction I've directly observed being false half the time, those
    /// assertions were removed — this is unresolved, not fixed. See docs/ROADMAP_v4.2.0.md Pillar 3
    /// for the actual finding and the recommended next step (embed a static, non-variable release
    /// instead, which sidesteps the question rather than continuing to chase this down).
    /// </summary>
    public class EmbeddedFontResolutionTests
    {
        // A real WPF Application registers the "pack" URI scheme as part of its own static
        // initialization — every "./Fonts/#Name"-style reference in this app's XAML relies on
        // that having already happened. Nothing in a bare xunit host ever constructs an
        // Application, so pack:// URIs fail here with "Invalid port specified" (System.Uri falling
        // back to generic parsing rules for a scheme it doesn't recognize) unless the registration
        // is forced explicitly first. Touching PackUriHelper's static surface does that — this part
        // is reliable and reproduces the same way in isolation and in the full suite.
        static EmbeddedFontResolutionTests()
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        }

        // Constructed from code in a *different* assembly than the font is embedded in (this test
        // project, not OmenCoreApp), so — unlike XAML's "./Fonts/#Name" shorthand, which resolves
        // relative to the containing XAML file's own assembly automatically — the target assembly
        // has to be named explicitly. The short name is "OmenCore" (the configured <AssemblyName>
        // in OmenCoreApp.csproj), not "OmenCoreApp" (the project folder name) — confirmed against
        // the .csproj, not guessed.
        private static FontFamily CreateEmbeddedAppFont() =>
            new(new Uri("pack://application:,,,/"), "/OmenCore;component/Fonts/#Roboto Condensed");

        [Fact]
        public void EmbeddedRobotoCondensed_ResolvesToAtLeastOneTypeface()
        {
            // The pack URI + assembly name + internal family name are all correct: WPF finds
            // *something* for this reference, consistently, regardless of run order. This alone
            // does not prove text will render correctly — see this file's class-level doc comment
            // for the unresolved glyph-resolution question this test deliberately does not claim
            // an answer to.
            var family = CreateEmbeddedAppFont();

            family.GetTypefaces().Should().NotBeEmpty(
                "an empty result would mean the pack URI, relative path, or internal family name is wrong " +
                "and WPF silently fell through to nothing rather than throwing");
        }

        [Fact]
        public void ControlCheck_KnownSystemFont_GlyphTypefaceResolutionWorksInThisHost()
        {
            // Confirms glyph typeface resolution works at all in this host for an ordinary,
            // non-variable system font — kept as a baseline sanity check even though the
            // variable-font-specific assertions that used to sit alongside it were removed for
            // being non-deterministic (see class-level doc comment).
            var family = new FontFamily("Segoe UI");

            var resolvedAny = false;
            foreach (var typeface in family.GetTypefaces())
            {
                if (typeface.TryGetGlyphTypeface(out _))
                {
                    resolvedAny = true;
                    break;
                }
            }

            resolvedAny.Should().BeTrue("Segoe UI is a completely standard, non-variable system font and should always resolve here");
        }
    }
}
