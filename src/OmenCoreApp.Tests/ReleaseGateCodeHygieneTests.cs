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
            "App.xaml.cs:627", // shifted from :605 after 3.6 planning branch UI wiring edits
            "App.xaml.cs:1057",
            "AmdGpuService.cs:409", // shifted from :371 after adding DescribeAdlResult/ADL error constants
            "EcAccessFactory.cs:216",
            "FanController.cs:415", // shifted from :412 after GetBridgeTemperatures bare-catch fix (3.9.0)
            "FanController.cs:772", // shifted from :769 after GetBridgeTemperatures bare-catch fix (3.9.0)
            "FanControllerFactory.cs:157", // shifted from :151
            "FanControllerFactory.cs:176", // shifted from :170
            "FanControllerFactory.cs:1168", // shifted from :1165 after WMI wrapper external-reset status properties
            "FanControllerFactory.cs:1200", // shifted from :1197 after WMI wrapper external-reset status properties
            "FanControllerFactory.cs:1218", // shifted from :1215 after WMI wrapper external-reset status properties
            // HardwareWorkerClient.cs:468 — resolved in v3.8.2: the bare catch in
            // ReleaseOwnedWorkerProcessHandle was converted to a typed, logging catch
            // alongside the pipe-desync hang fix (BUG-3820-001).
            "LibreHardwareMonitorImpl.cs:2090", // shifted from :2106 after 3.7.0 live temperature projection cleanup
            "LibreHardwareMonitorImpl.cs:2219", // shifted from :2235 after 3.7.0 live temperature projection cleanup
            "LibreHardwareMonitorImpl.cs:2245", // shifted from :2261 after 3.7.0 live temperature projection cleanup
            "LibreHardwareMonitorImpl.cs:2251", // shifted from :2267 after 3.7.0 live temperature projection cleanup
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
            "ThermalSensorProvider.cs:86", // shifted from :82 after 3.7.0 live temperature projection cleanup
            "ThermalSensorProvider.cs:101", // shifted from :96 after 3.7.0 live temperature projection cleanup
            "WmiBiosMonitor.cs:332",
            "WmiBiosMonitor.cs:563",
            "WmiBiosMonitor.cs:1007",
            "WmiBiosMonitor.cs:1478",
            "WmiBiosMonitor.cs:1521",
            "WmiFanController.cs:172", // shifted from :170 after v3.6.1 countdown-extension throttle
            "WmiFanController.cs:186", // shifted from :184 after v3.6.1 countdown-extension throttle
            "WmiFanController.cs:200", // shifted from :198 after v3.6.1 countdown-extension throttle
            // DiagnosticLoggingService.cs:97/333/336 — resolved in v3.8.2: converted to typed
            // catches (Debug.WriteLine for single-point failures, silent typed skip for the
            // per-process inspection loop) so the diagnostic subsystem's own failures are no
            // longer invisible — the same "logs just stop" lesson behind BUG-3820-001.
            "FanService.cs:2044", // shifted from :1983 after #25 RPM-state propagation + monitor-loop allocation cleanup
            "GameLibraryService.cs:269",
            "GameLibraryService.cs:335",
            "GameLibraryService.cs:392",
            "GameLibraryService.cs:495",
            "GameLibraryService.cs:540",
            "KeyboardLightingService.cs:240", // shifted from :238 after EC coordinator injection into V2 backend
            "NotificationService.cs:522",
            "OmenKeyService.cs:369",
            "OsdService.cs:373", // shifted from :357
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
            "StorageOptimizer.cs:148", // shifted from :147 by an added `using` line; pre-existing bare catch in DetectSSD(), not introduced by the last-access-timestamp verification fix
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

        private static string? FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "OmenCore.sln")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "installer")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return null;
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

        // ─── v3.6.1 runtime reduction gates ──────────────────────────────────

        [Fact]
        public void RuntimePresentation_NoLegacyFanCurveBypassInMainViewModel()
        {
            var root = GetMainSourceRoot();
            if (!Directory.Exists(root))
                return;

            var path = Path.Combine(root, "ViewModels", "MainViewModel.cs");
            var content = File.ReadAllText(path);

            content.Should().NotContain("ApplyFanCurveCommand",
                "custom fan-curve application belongs to FanControlViewModel so validation and verification are not bypassed");
            content.Should().NotContain("_fanService.ApplyCustomCurve",
                "MainViewModel must remain a presentation mapper and not directly mutate fan curves");
        }

        [Fact]
        public void RuntimePresentation_GeneralViewModelHasNoIndependentDispatcherTimer()
        {
            var root = GetMainSourceRoot();
            if (!Directory.Exists(root))
                return;

            var path = Path.Combine(root, "ViewModels", "GeneralViewModel.cs");
            var content = File.ReadAllText(path);

            content.Should().NotContain("DispatcherTimer",
                "GeneralViewModel should subscribe to the centralized monitoring sample stream instead of owning a 1-second presentation timer");
        }

        [Fact]
        public void RuntimePresentation_GeneralViewModelUsesTelemetryProjectionRateLimit()
        {
            var root = GetMainSourceRoot();
            if (!Directory.Exists(root))
                return;

            var path = Path.Combine(root, "ViewModels", "GeneralViewModel.cs");
            var content = File.ReadAllText(path);

            content.Should().Contain("UiProjectionMinInterval",
                "GeneralViewModel telemetry projection must be rate-limited to keep the UI quiet under high-frequency monitoring");
            content.Should().Contain("ShouldProjectMonitoringSample",
                "GeneralViewModel must coalesce minor updates before notifying bindings");
        }

        [Fact]
        public void RuntimePresentation_DashboardProjectionAvoidsUnboundedUiLoop()
        {
            var root = GetMainSourceRoot();
            if (!Directory.Exists(root))
                return;

            var path = Path.Combine(root, "ViewModels", "DashboardViewModel.cs");
            var content = File.ReadAllText(path);

            var onSampleStart = content.IndexOf("private void OnSampleUpdated", StringComparison.Ordinal);
            var onSampleEnd = content.IndexOf("private void UpdateFanCurvePoints", StringComparison.Ordinal);
            onSampleStart.Should().BeGreaterThan(0);
            onSampleEnd.Should().BeGreaterThan(onSampleStart);

            var onSampleSection = content.Substring(onSampleStart, onSampleEnd - onSampleStart);
            onSampleSection.Should().NotContain("while (true)",
                "dashboard telemetry projection must process coalesced updates incrementally and yield back to the dispatcher");

            content.Should().Contain("UiProjectionMinInterval",
                "dashboard UI projection should be decoupled from raw telemetry cadence");
            content.Should().Contain("ShouldProjectSampleToUi",
                "dashboard must gate minor sample noise to prevent binding and layout storms");
            content.Should().Contain("SetTelemetryProjectionEnabled",
                "dashboard projection must become explicitly suppressible when the surface is hidden or minimized");
            content.Should().Contain("!_telemetryProjectionEnabled",
                "dashboard must skip projection work while hidden instead of continuously mutating telemetry collections");
        }

        [Fact]
        public void RuntimeDiagnostics_DiagnosticExportEcReadsUseCoordinator()
        {
            var root = GetMainSourceRoot();
            if (!Directory.Exists(root))
                return;

            var path = Path.Combine(root, "Services", "Diagnostics", "DiagnosticExportService.cs");
            var content = File.ReadAllText(path);

            content.Should().Contain("RuntimeEcOperationCoordinator",
                "support-bundle EC snapshots should share the runtime EC serialization gate");
            content.Should().NotContain("byte value = ecAccess.ReadByte",
                "diagnostic export loops must not perform raw EC reads outside ReadDiagnosticEcByte");
        }

        [Fact]
        public void RuntimePresentation_TrayRefreshSkipsRedundantRenderedState()
        {
            var root = GetMainSourceRoot();
            if (!Directory.Exists(root))
                return;

            var path = Path.Combine(root, "Utils", "TrayIconService.cs");
            var content = File.ReadAllText(path);

            content.Should().Contain("_lastTooltipText",
                "tray refresh should cache its last rendered tooltip so fixed timer ticks can skip redundant work");
            content.Should().Contain("SetMenuHeaderIfChanged",
                "tray refresh should avoid repeating identical menu header mutations on every timer tick");
            content.Should().Contain("_lastRenderedBadgeTemperature",
                "tray icon badge regeneration should be bounded by visible temperature changes instead of every refresh tick");
        }

        [Fact]
        public void Installer_PawnIoBundledInstallerRunsSilentlyFromEmbeddedTempFile()
        {
            var repoRoot = FindRepoRoot();
            if (repoRoot == null)
                return;

            var path = Path.Combine(repoRoot, "installer", "OmenCoreInstaller.iss");
            var content = File.ReadAllText(path);

            content.Should().Contain("Source: \"PawnIO_setup.exe\"; DestDir: \"{tmp}\"",
                "the release installer must embed the PawnIO setup and extract it to the installer temp folder");
            content.Should().Contain("Filename: \"{tmp}\\PawnIO_setup.exe\"; Parameters: \"-install -silent\"",
                "PawnIO should receive the required install verb and run silently from the bundled temp copy when the task is selected");
            content.Should().Contain("Flags: waituntilterminated runhidden",
                "the PawnIO sub-installer should run without surfacing a separate installer window");
            content.Should().Contain("Check: not IsPawnIOInstalled",
                "the sub-installer should still be skipped on machines where PawnIO is already present");
            content.Should().NotContain("PawnIOInstallerExists",
                "runtime checks against {src} can fail for a single bundled setup EXE and silently skip PawnIO extraction/install");
        }

        [Fact]
        public void RuntimePresentation_QuickPopupRefreshSkipsRedundantRenderedState()
        {
            var root = GetMainSourceRoot();
            if (!Directory.Exists(root))
                return;

            var path = Path.Combine(root, "Views", "QuickPopupWindow.xaml.cs");
            var content = File.ReadAllText(path);

            content.Should().Contain("PopupTelemetryDisplayState",
                "quick popup refresh should cache the last rendered telemetry state so visible timer ticks can skip no-op redraws");
            content.Should().Contain("_lastRenderedTelemetryState",
                "quick popup should keep a render-state fingerprint instead of rewriting text on every refresh tick");
        }

        // ─── Source-root discovery smoke test ─────────────────────────────────

        [Fact]
        public void RuntimePresentation_MainWindowChromeUsesStableAsciiControls()
        {
            var root = GetMainSourceRoot();
            if (!Directory.Exists(root))
                return;

            var xamlPath = Path.Combine(root, "Views", "MainWindow.xaml");
            var codePath = Path.Combine(root, "Views", "MainWindow.xaml.cs");
            var xaml = File.ReadAllText(xamlPath);
            var code = File.ReadAllText(codePath);

            xaml.Should().Contain("Content=\"-\"",
                "window chrome should avoid glyphs that can mojibake under mixed editor encodings");
            xaml.Should().Contain("Content=\"[ ]\"",
                "the normal maximize button should use ASCII-stable chrome text");
            xaml.Should().Contain("Content=\"x\"",
                "the close button should avoid glyphs that can mojibake under mixed editor encodings");
            code.Should().Contain("MaximizeButton.Content = WindowState == WindowState.Maximized ? \"[]\" : \"[ ]\";",
                "runtime maximize/restore updates should stay ASCII-stable");
            code.Should().Contain("catch (InvalidOperationException ex)",
                "expected title-bar drag races should be logged explicitly instead of swallowed by a bare catch");
        }

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
