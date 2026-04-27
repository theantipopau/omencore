// Area E — Release-gate code hygiene checks (no-hardware session 2026-04-16)
// Static analysis via file I/O — verify that forbidden code patterns have not been
// reintroduced after the STEP-03/04/05/06 exception-handling cleanup.
//
// Patterns governed by this gate:
//   1. Bare catch {} — silently discards all exceptions, hides crashes
//      v3.3.1 strategy: 83 pre-existing violations are in KnownBareCatchViolations below
//      and are treated as advisory only. Any violation NOT in that set fails the build.
//      Full historical cleanup is deferred to a post-v3.3.1 release.
//   2. ex.Message.Contains("...") — English string matching for exception routing (STEP-03)
//      This check remains fully blocking; zero tolerance, no known baseline.
//
// These tests do NOT require physical OMEN hardware.
// They are intended to run in CI to act as regression guards.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace OmenCoreApp.Tests
{
    public class ReleaseGateCodeHygieneTests
    {
        private readonly ITestOutputHelper _output;

        public ReleaseGateCodeHygieneTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // ─── v3.3.1 known bare-catch baseline (83 violations, audited 2026-04-16) ──────
        // These were present in the source tree before the v3.3.1 work began and are
        // deferred for cleanup to a later release. They MUST NOT grow. Any file:line not
        // in this set is a new violation introduced after the baseline was captured and
        // will fail NoBareCatchBraces_NewViolations_Blocking.
        //
        // Line numbers are recorded at the point the catch keyword appears.
        // If a line shifts due to unrelated edits in the same file, a shifted violation
        // may briefly appear as "new" (false-positive) — resolve by updating the baseline
        // in the same PR that edits the file, with a comment explaining the shift.
        private static readonly HashSet<string> KnownBareCatchViolations = new(StringComparer.Ordinal)
        {
            "App.xaml.cs:596",
            "App.xaml.cs:1057",
            "AmdGpuService.cs:371",
            "EcAccessFactory.cs:216",
            "FanController.cs:412",
            "FanController.cs:769",
            "FanControllerFactory.cs:151",
            "FanControllerFactory.cs:170",
            "FanControllerFactory.cs:1165",
            "FanControllerFactory.cs:1197",
            "FanControllerFactory.cs:1215",
            "HardwareWorkerClient.cs:468",
            "LibreHardwareMonitorImpl.cs:2106", // shifted from :2101 after timeout-path cleanup edits
            "LibreHardwareMonitorImpl.cs:2235", // shifted from :2230 after timeout-path cleanup edits
            "LibreHardwareMonitorImpl.cs:2261", // shifted from :2256 after timeout-path cleanup edits
            "LibreHardwareMonitorImpl.cs:2267", // shifted from :2262 after timeout-path cleanup edits
            "MsrAccessFactory.cs:76",
            "NvapiService.cs:1282",
            "NvapiService.cs:1299",
            "NvapiService.cs:1310",
            "NvapiService.cs:1338",
            "NvapiService.cs:1355",
            "NvapiService.cs:1366",
            "NvapiService.cs:1393",
            "NvapiService.cs:1403",
            "OghServiceProxy.cs:292",
            "OghServiceProxy.cs:323",
            "OmenDesktopRgbService.cs:393",
            "OmenDesktopRgbService.cs:1244",
            "PawnIOEcAccess.cs:236",
            "PawnIOMsrAccess.cs:112",
            "RyzenSmu.cs:143",
            "ThermalSensorProvider.cs:82", // shifted from :78 after timeout-path cleanup edits
            "ThermalSensorProvider.cs:96", // shifted from :92 after timeout-path cleanup edits
            "WmiBiosMonitor.cs:332",
            "WmiBiosMonitor.cs:563",
            "WmiBiosMonitor.cs:1007",
            "WmiBiosMonitor.cs:1478",
            "WmiBiosMonitor.cs:1521",
            "WmiFanController.cs:137", // shifted from :125 after max-mode maintenance fields
            "WmiFanController.cs:151", // shifted from :139 after max-mode maintenance fields
            "WmiFanController.cs:165", // shifted from :153 after max-mode maintenance fields
            "DiagnosticLoggingService.cs:97",
            "DiagnosticLoggingService.cs:333",
            "DiagnosticLoggingService.cs:336",
            "FanService.cs:2044", // shifted from :1983 after #25 RPM-state propagation + monitor-loop allocation cleanup
            "GameLibraryService.cs:269",
            "GameLibraryService.cs:335",
            "GameLibraryService.cs:392",
            "GameLibraryService.cs:495",
            "GameLibraryService.cs:540",
            "KeyboardLightingService.cs:218",
            "NotificationService.cs:522",
            "OmenKeyService.cs:369",
            "OsdService.cs:357",
            "TemperatureRgbService.cs:270",
            "TrayIconService.cs:134",
            "LightingViewModel.cs:2010",
            "LightingViewModel.cs:2028",
            "SettingsViewModel.cs:3079",
            "SettingsViewModel.cs:3414",
            "SettingsViewModel.cs:3445",
            "SettingsViewModel.cs:3751",
            "SettingsViewModel.cs:3757",
            "SettingsViewModel.cs:4356",
            "CorsairHidDirect.cs:252",
            "CorsairHidDirect.cs:299",
            "CorsairHidDirect.cs:403",
            "CorsairHidDirect.cs:421",
            "CorsairHidDirect.cs:773",
            "CorsairHidDirect.cs:786",
            "CorsairHidDirect.cs:842",
            "CorsairHidDirect.cs:861",
            "KeyboardLightingServiceV2.cs:551",
            "LogitechHidDirect.cs:269",
            "LogitechHidDirect.cs:494",
            "LogitechHidDirect.cs:541",
            "OpenRgbProvider.cs:447",
            "RgbManager.cs:98",
            "RgbManager.cs:164",
            "RgbManager.cs:186",
            "RgbManager.cs:208",
            "StorageOptimizer.cs:147",
        };
        // Resolve src/OmenCoreApp relative to the test assembly's output directory.
        // The test binary sits at src/OmenCoreApp.Tests/bin/<cfg>/<tfm>/
        // Walking up 4 levels reaches src/, then appending OmenCoreApp reaches the source tree.
        private static string GetMainSourceRoot()
        {
            var testBin = AppContext.BaseDirectory;
            // Walk up until we find the solution root (contains OmenCore.sln) or src/
            var dir = new DirectoryInfo(testBin);
            for (var i = 0; i < 10; i++)
            {
                if (dir == null || dir.Parent == null)
                    break;

                var candidate = Path.Combine(dir.FullName, "src", "OmenCoreApp");
                if (Directory.Exists(candidate))
                    return candidate;

                // Also try current dir itself containing OmenCoreApp
                candidate = Path.Combine(dir.FullName, "OmenCoreApp");
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "OmenCoreApp.csproj")))
                    return candidate;

                dir = dir.Parent;
            }

            // Fallback: walk from the known workspace root via env or well-known path
            var workspaceRoot = Environment.GetEnvironmentVariable("OMENCORE_REPO_ROOT")
                ?? Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", "..", "..", "OmenCoreApp"));

            return workspaceRoot;
        }

        private static IEnumerable<string> GetCSharpSourceFiles()
        {
            var root = GetMainSourceRoot();

            if (!Directory.Exists(root))
            {
                // CI environment where source isn't adjacent to test output — skip gracefully.
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                         && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));
        }

        // ─── Bare catch {} — helper ───────────────────────────────────────────

        private static List<string> FindBareCatchViolations()
        {
            var barePattern = new Regex(@"catch\s*\{\s*\}", RegexOptions.Multiline);
            var violations = new List<string>();

            foreach (var file in GetCSharpSourceFiles())
            {
                var content = File.ReadAllText(file);
                var matches = barePattern.Matches(content);
                foreach (Match m in matches)
                {
                    var lineNumber = content[..m.Index].Count(c => c == '\n') + 1;
                    violations.Add($"{Path.GetFileName(file)}:{lineNumber}");
                }
            }

            return violations;
        }

        // ─── Bare catch {} — ADVISORY (known pre-existing violations) ─────────
        // Always passes. Reports the count of known deferred violations so the number
        // is visible in CI output. This test will naturally shrink toward zero as
        // individual files are cleaned up in future releases.

        [Fact]
        public void NoBareCatchBraces_KnownViolations_Advisory()
        {
            var all = FindBareCatchViolations();
            var known = all.Where(v => KnownBareCatchViolations.Contains(v)).ToList();
            var unexpected = all.Where(v => !KnownBareCatchViolations.Contains(v)).ToList();

            _output.WriteLine($"[ADVISORY] Bare catch {{}} — known deferred violations: {known.Count} / {KnownBareCatchViolations.Count} baseline");
            if (known.Count > 0)
            {
                _output.WriteLine("  Known (advisory, deferred to vNext cleanup):");
                foreach (var v in known.OrderBy(x => x))
                    _output.WriteLine($"    {v}");
            }

            if (known.Count < KnownBareCatchViolations.Count)
            {
                var resolved = KnownBareCatchViolations.Except(all).ToList();
                _output.WriteLine($"  Resolved since baseline (good!): {resolved.Count}");
                foreach (var v in resolved.OrderBy(x => x))
                    _output.WriteLine($"    {v}");
            }

            // Never fails — purely informational.
            // If unexpected violations exist, NoBareCatchBraces_NewViolations_Blocking will fail.
            _ = unexpected; // surfaced by the blocking test below
        }

        // ─── Bare catch {} — BLOCKING (new violations not in known baseline) ──
        // Fails if any bare catch {} is found that is NOT in KnownBareCatchViolations.
        // This prevents new technical debt from being added while the historical
        // backlog is still being cleaned up.

        [Fact]
        public void NoBareCatchBraces_NewViolations_Blocking()
        {
            var all = FindBareCatchViolations();
            var newViolations = all.Where(v => !KnownBareCatchViolations.Contains(v)).ToList();

            if (newViolations.Count > 0)
            {
                _output.WriteLine($"[FAIL] {newViolations.Count} new bare catch {{}} violation(s) not in the v3.3.1 baseline:");
                foreach (var v in newViolations.OrderBy(x => x))
                    _output.WriteLine($"  NEW: {v}");
            }
            else
            {
                _output.WriteLine("[PASS] No new bare catch {} violations introduced beyond the known baseline.");
            }

            newViolations.Should().BeEmpty(
                "new bare catch {{}} blocks must not be introduced; " +
                "each exception must be typed, logged, or explicitly documented as intentional. " +
                "To add a justified exception, add it to KnownBareCatchViolations with a comment. " +
                "New violations: " + string.Join(", ", newViolations));
        }

        // ─── ex.Message.Contains — BLOCKING (zero tolerance, no known baseline) ─

        [Fact]
        public void NoExMessageContains_InMainSourceTree()
        {
            // Matches patterns like: ex.Message.Contains(  or  e.Message.Contains(
            var pattern = new Regex(@"\b\w+\.Message\.Contains\s*\(", RegexOptions.Multiline);

            var violations = new List<string>();

            foreach (var file in GetCSharpSourceFiles())
            {
                // Skip test files that might legitimately check log messages
                if (file.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                var content = File.ReadAllText(file);
                var matches = pattern.Matches(content);
                foreach (Match m in matches)
                {
                    var lineNumber = content[..m.Index].Count(c => c == '\n') + 1;
                    violations.Add($"{Path.GetFileName(file)}:{lineNumber}");
                }
            }

            if (violations.Count > 0)
            {
                _output.WriteLine($"[FAIL] {violations.Count} ex.Message.Contains violation(s):");
                foreach (var v in violations.OrderBy(x => x))
                    _output.WriteLine($"  {v}");
            }
            else
            {
                _output.WriteLine("[PASS] No ex.Message.Contains violations found.");
            }

            violations.Should().BeEmpty(
                "exception routing via ex.Message.Contains() must not appear; " +
                "use typed catch clauses or HResult checks instead (STEP-03). " +
                "Violations: " + string.Join(", ", violations));
        }

        // ─── Source-root discovery smoke test ─────────────────────────────────

        [Fact]
        public void SourceRoot_ContainsExpectedFiles()
        {
            var root = GetMainSourceRoot();

            if (!Directory.Exists(root))
            {
                // Source tree not available in this environment (e.g. binary-only CI) — skip.
                return;
            }

            // Verify the discovery logic found the right directory
            var projectFile = Path.Combine(root, "OmenCoreApp.csproj");
            File.Exists(projectFile).Should().BeTrue(
                $"discovered source root '{root}' must contain OmenCoreApp.csproj");
        }
    }
}
