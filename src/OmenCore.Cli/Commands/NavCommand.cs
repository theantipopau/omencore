using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using OmenCore.Services;

namespace OmenCore.Cli.Commands;

/// <summary>
/// nav - cycles every page in the app and reports how long each switch took.
///
/// The number this prints is measured inside the app, not here: a tab switch is a WPF layout pass
/// over a view that is already constructed (see MainWindow.EnsureTabContentCreated), and a console
/// process cannot host that. So this launches OmenCore.exe with --nav-cycle=N, the window walks its
/// own pages through the normal selection path, and the timings land in
/// NavigationTimingReport.DefaultPath for this command to read back.
///
/// Elevation: both executables carry requireAdministrator in their manifests, so a child started
/// from an elevated CLI inherits the elevated token and Windows shows no second prompt. Started
/// from a non-elevated shell, the app's own manifest raises UAC once.
///
/// Single instance: if OmenCore is already running, the launch hands off to that instance and exits
/// without cycling, so no report is written - this says so rather than reporting an empty table.
/// </summary>
public static class NavCommand
{
    public static Command Create()
    {
        var command = new Command("nav", "Cycle every page in the app and report how long each switch took");

        var appOption = new Option<string?>(
            aliases: new[] { "--app" },
            description: "Path to OmenCore.exe (default: found next to this tool, or in a sibling OmenCoreApp build output)");

        var passesOption = new Option<int>(
            aliases: new[] { "--passes", "-p" },
            getDefaultValue: () => 3,
            description: "How many times to cycle through the pages");

        var reportOption = new Option<string?>(
            aliases: new[] { "--report" },
            description: "Read timings from this file instead of %LOCALAPPDATA%\\OmenCore\\nav-report.json");

        var timeoutOption = new Option<int>(
            aliases: new[] { "--timeout" },
            getDefaultValue: () => 180,
            description: "Seconds to wait for the app to finish cycling");

        var jsonOption = new Option<bool>(
            aliases: new[] { "--json", "-j" },
            description: "Output in JSON format");

        command.AddOption(appOption);
        command.AddOption(passesOption);
        command.AddOption(reportOption);
        command.AddOption(timeoutOption);
        command.AddOption(jsonOption);

        command.SetHandler(Handle, appOption, passesOption, reportOption, timeoutOption, jsonOption);
        return command;
    }

    private static void Handle(string? appPath, int passes, string? reportPath, int timeoutSeconds, bool json)
    {
        string? executable = ResolveAppPath(appPath);
        if (executable == null)
        {
            Console.Error.WriteLine(string.IsNullOrWhiteSpace(appPath)
                ? "Could not find OmenCore.exe next to this tool or in a sibling OmenCoreApp build output. Pass --app <path to OmenCore.exe>."
                : $"--app: no such file: {appPath}");
            Environment.Exit(1);
            return;
        }

        string target = string.IsNullOrWhiteSpace(reportPath) ? NavigationTimingReport.DefaultPath : reportPath!;
        passes = Math.Clamp(passes, 1, 20);

        // A report from an earlier run must not be mistaken for this one's: if the cycle fails to
        // write, the difference between "old numbers" and "no numbers" is the whole diagnosis.
        try
        {
            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not clear the previous report at {target}: {ex.Message}");
            Environment.Exit(2);
            return;
        }

        var startInfo = new ProcessStartInfo { UseShellExecute = false };
        if (executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = "dotnet";
            startInfo.Arguments = $"\"{executable}\" --nav-cycle={passes}";
        }
        else
        {
            startInfo.FileName = executable;
            startInfo.Arguments = $"--nav-cycle={passes}";
        }

        if (!json)
        {
            Console.WriteLine();
            Console.WriteLine($"Cycling {passes}x through the pages in {Path.GetFileName(executable)}...");
        }

        int exitCode;
        using (var process = Process.Start(startInfo))
        {
            if (process == null)
            {
                Console.Error.WriteLine($"Could not start {executable}");
                Environment.Exit(1);
                return;
            }

            if (!process.WaitForExit(Math.Max(10, timeoutSeconds) * 1000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Already gone between the timeout and the kill - the timeout is the answer either way.
                }

                Console.Error.WriteLine($"The app did not finish cycling within {timeoutSeconds} s; it was stopped. Raise --timeout if the machine is slow.");
                Environment.Exit(2);
                return;
            }

            exitCode = process.ExitCode;
        }

        var report = NavigationTimingReport.TryRead(target);
        if (report == null || report.Pages.Count == 0)
        {
            Console.Error.WriteLine(exitCode != 0
                ? $"The app exited with code {exitCode} before writing timings - see the log in %LOCALAPPDATA%\\OmenCore\\logs."
                : "No timings were written. If OmenCore is already running, this launch handed off to that instance instead of cycling a second window: close the running copy and try again.");
            Environment.Exit(1);
            return;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        Console.WriteLine();
        Console.WriteLine("=== OmenCore CLI - Navigation timings ===");
        Console.WriteLine();
        Console.WriteLine($"  Pages:   {report.Pages.Count}   passes: {report.Passes}   written: {report.GeneratedAtUtc}");
        Console.WriteLine($"  Report:  {target}");
        if (report.StartupTimeToInteractiveMs is double startup)
        {
            Console.WriteLine($"  Startup: {startup:F0} ms to interactive");
        }
        Console.WriteLine();
        Console.WriteLine($"  {"Page",-24} {"samples",8} {"avg ms",8} {"max ms",8}");
        foreach (var page in report.Pages)
        {
            Console.WriteLine($"  {page.Page,-24} {page.Samples,8} {page.AverageMs,8:F1} {page.MaxMs,8:F1}");
        }

        if (report.Slowest is { } slowest)
        {
            Console.WriteLine();
            Console.WriteLine($"  Slowest: {slowest.Page} - {slowest.AverageMs:F1} ms average, {slowest.MaxMs:F1} ms worst");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// --app wins. Otherwise look for OmenCore.exe beside this executable (an installed layout puts
    /// both in one directory), then walk up a few levels for a sibling OmenCoreApp build, which is
    /// the layout in a source checkout.
    /// </summary>
    private static string? ResolveAppPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return File.Exists(explicitPath) ? explicitPath : null;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int depth = 0; depth < 6 && directory != null; depth++, directory = directory.Parent)
        {
            string beside = Path.Combine(directory.FullName, "OmenCore.exe");
            if (File.Exists(beside))
            {
                return beside;
            }

            string appBuild = Path.Combine(directory.FullName, "OmenCoreApp", "bin");
            if (!Directory.Exists(appBuild))
            {
                continue;
            }

            try
            {
                string? newest = Directory
                    .GetFiles(appBuild, "OmenCore.exe", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (newest != null)
                {
                    return newest;
                }
            }
            catch (Exception)
            {
                // Unreadable build directory - keep walking.
            }
        }

        return null;
    }
}
