using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;

namespace OmenCore.Avalonia;

/// <summary>
/// Application entry point.
/// </summary>
internal sealed class Program
{
    /// <summary>
    /// Initialization code - ensure it's called before any Avalonia functionality.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--version" || args[0] == "-V"))
        {
            var v = typeof(Program).Assembly.GetName().Version;
            var versionText = v is null ? "unknown" : $"{v.Major}.{v.Minor}.{v.Build}";
            Console.WriteLine($"OmenCore GUI v{versionText}");
            return;
        }

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            ReportStartupFailure(ex);
            Environment.ExitCode = 1;
        }
    }

    /// <summary>
    /// Avalonia configuration - used by designer and runtime.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(GetLinuxX11PlatformOptions())
            .WithInterFont()
            .LogToTrace();

    private static X11PlatformOptions GetLinuxX11PlatformOptions()
    {
        if (!OperatingSystem.IsLinux())
        {
            return new X11PlatformOptions();
        }

        return new X11PlatformOptions
        {
            RenderingMode = GetLinuxRenderingModes(),
            EnableSessionManagement = false
        };
    }

    private static IReadOnlyList<X11RenderingMode> GetLinuxRenderingModes()
    {
        var requestedMode = Environment.GetEnvironmentVariable("OMENCORE_GUI_RENDER_MODE");
        if (string.IsNullOrWhiteSpace(requestedMode))
        {
            return new[]
            {
                X11RenderingMode.Egl,
                X11RenderingMode.Glx,
                X11RenderingMode.Software
            };
        }

        return requestedMode.Trim().ToLowerInvariant() switch
        {
            "software" => new[]
            {
                X11RenderingMode.Software
            },
            "glx" => new[]
            {
                X11RenderingMode.Glx,
                X11RenderingMode.Software
            },
            "egl" => new[]
            {
                X11RenderingMode.Egl,
                X11RenderingMode.Glx,
                X11RenderingMode.Software
            },
            "vulkan" => new[]
            {
                X11RenderingMode.Vulkan,
                X11RenderingMode.Egl,
                X11RenderingMode.Glx,
                X11RenderingMode.Software
            },
            _ => new[]
            {
                X11RenderingMode.Egl,
                X11RenderingMode.Glx,
                X11RenderingMode.Software
            }
        };
    }

    private static void ReportStartupFailure(Exception ex)
    {
        var logPath = TryWriteStartupLog(ex);
        var baseException = ex.GetBaseException();

        Console.Error.WriteLine("OmenCore GUI failed to start.");
        Console.Error.WriteLine($"Reason: {baseException.Message}");

        if (OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("If the terminal mentions GLX or llvmpipe, retry with OMENCORE_GUI_RENDER_MODE=software.");
            Console.Error.WriteLine("Example: sudo env OMENCORE_GUI_RENDER_MODE=software DISPLAY=$DISPLAY XAUTHORITY=$XAUTHORITY ./omencore-gui");
            Console.Error.WriteLine("The SESSION_MANAGER warning on X11 is informational and can be ignored.");
        }

        if (!string.IsNullOrWhiteSpace(logPath))
        {
            Console.Error.WriteLine($"Startup log: {logPath}");
        }
    }

    private static string? TryWriteStartupLog(Exception ex)
    {
        try
        {
            var configRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(configRoot))
            {
                return null;
            }

            var logDirectory = Path.Combine(configRoot, "OmenCore", "logs");
            Directory.CreateDirectory(logDirectory);

            var logPath = Path.Combine(logDirectory, "gui-startup.log");
            var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";

            var logText = string.Join(Environment.NewLine, new[]
            {
                $"timestamp={DateTimeOffset.UtcNow:O}",
                $"version={version}",
                $"os={Environment.OSVersion}",
                $"session_type={Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? string.Empty}",
                $"display={Environment.GetEnvironmentVariable("DISPLAY") ?? string.Empty}",
                $"wayland_display={Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? string.Empty}",
                $"render_mode_override={Environment.GetEnvironmentVariable("OMENCORE_GUI_RENDER_MODE") ?? string.Empty}",
                ex.ToString(),
                string.Empty
            });

            File.WriteAllText(logPath, logText);
            return logPath;
        }
        catch
        {
            return null;
        }
    }
}
