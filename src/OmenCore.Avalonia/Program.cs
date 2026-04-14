using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OmenCore.Avalonia;

/// <summary>
/// Application entry point.
/// </summary>
internal sealed class Program
{
    private const string RenderModeEnvVar = "OMENCORE_GUI_RENDER_MODE";
    private const string RenderRetryEnvVar = "OMENCORE_GUI_RENDER_RETRY";

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
            PrepareLinuxDesktopEnvironment();
            ApplyPersistedLinuxRenderMode();
            StartWithLinuxFallback(args);
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
        var requestedMode = Environment.GetEnvironmentVariable(RenderModeEnvVar);
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

    private static void StartWithLinuxFallback(string[] args)
    {
        var initialMode = GetEffectiveRenderMode();

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
            RecordRendererStartupSuccess(initialMode);
            return;
        }
        catch (Exception ex) when (ShouldRetryWithSoftware(ex, initialMode))
        {
            RecordRendererStartupFailure(ex, initialMode);

            Console.Error.WriteLine("OmenCore: renderer initialization failed, retrying with software mode.");
            Environment.SetEnvironmentVariable(RenderModeEnvVar, "software");
            Environment.SetEnvironmentVariable(RenderRetryEnvVar, "1");

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);

            RecordRendererStartupSuccess("software");
            return;
        }
        catch (Exception ex)
        {
            RecordRendererStartupFailure(ex, initialMode);
            throw;
        }
    }

    private static bool ShouldRetryWithSoftware(Exception ex, string initialMode)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        if (string.Equals(Environment.GetEnvironmentVariable(RenderRetryEnvVar), "1", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(initialMode, "software", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var explicitMode = Environment.GetEnvironmentVariable(RenderModeEnvVar);
        if (!string.IsNullOrWhiteSpace(explicitMode) &&
            !string.Equals(explicitMode, "auto", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(explicitMode, "default", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsRendererStartupFailure(ex);
    }

    private static bool IsRendererStartupFailure(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains("GLX", StringComparison.OrdinalIgnoreCase)
            || text.Contains("EGL", StringComparison.OrdinalIgnoreCase)
            || text.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase)
            || text.Contains("OpenGL", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Vulkan", StringComparison.OrdinalIgnoreCase)
            || text.Contains("renderer", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Skia", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetEffectiveRenderMode()
    {
        var requested = Environment.GetEnvironmentVariable(RenderModeEnvVar);
        if (string.IsNullOrWhiteSpace(requested))
        {
            return "auto";
        }

        return requested.Trim().ToLowerInvariant();
    }

    private static void ApplyPersistedLinuxRenderMode()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RenderModeEnvVar)))
        {
            return;
        }

        var state = LoadRenderStartupState();
        if (state is null || string.IsNullOrWhiteSpace(state.LastKnownGoodRenderMode))
        {
            return;
        }

        var mode = state.LastKnownGoodRenderMode.Trim().ToLowerInvariant();
        Environment.SetEnvironmentVariable(RenderModeEnvVar, mode);
        Console.Error.WriteLine($"OmenCore: using persisted render mode '{mode}'.");
    }

    private static void RecordRendererStartupFailure(Exception ex, string mode)
    {
        if (!OperatingSystem.IsLinux() || !IsRendererStartupFailure(ex))
        {
            return;
        }

        var state = LoadRenderStartupState() ?? new RenderStartupState();
        state.ConsecutiveRendererStartupFailures++;
        state.LastFailureUtc = DateTimeOffset.UtcNow;

        if (state.ConsecutiveRendererStartupFailures >= 2 &&
            !string.Equals(mode, "software", StringComparison.OrdinalIgnoreCase))
        {
            state.LastKnownGoodRenderMode = "software";
        }

        SaveRenderStartupState(state);
    }

    private static void RecordRendererStartupSuccess(string mode)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var state = LoadRenderStartupState() ?? new RenderStartupState();
        state.ConsecutiveRendererStartupFailures = 0;
        state.LastFailureUtc = null;

        var normalized = mode.Trim().ToLowerInvariant();
        if (!string.Equals(normalized, "auto", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalized, "default", StringComparison.OrdinalIgnoreCase))
        {
            state.LastKnownGoodRenderMode = normalized;
        }

        SaveRenderStartupState(state);
    }

    private static RenderStartupState? LoadRenderStartupState()
    {
        try
        {
            var path = GetRenderStartupStatePath();
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RenderStartupState>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveRenderStartupState(RenderStartupState state)
    {
        try
        {
            var path = GetRenderStartupStatePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Best-effort state persistence only.
        }
    }

    private static string GetRenderStartupStatePath()
    {
        var configRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(configRoot))
        {
            return Path.Combine(Path.GetTempPath(), "OmenCore", "render-startup-state.json");
        }

        return Path.Combine(configRoot, "OmenCore", "render-startup-state.json");
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
            Console.Error.WriteLine("Example: OMENCORE_GUI_RENDER_MODE=software ./omencore-gui");
            Console.Error.WriteLine("If you launched with sudo, preserve user session vars: sudo --preserve-env=DISPLAY,XAUTHORITY,XDG_RUNTIME_DIR,DBUS_SESSION_BUS_ADDRESS,OMENCORE_GUI_RENDER_MODE ./omencore-gui");
            Console.Error.WriteLine("After repeated renderer failures, OmenCore can persist software mode as last-known-good.");
            Console.Error.WriteLine("The SESSION_MANAGER warning on X11 is informational and can be ignored.");
        }

        if (!string.IsNullOrWhiteSpace(logPath))
        {
            Console.Error.WriteLine($"Startup log: {logPath}");
        }
    }

    private static void PrepareLinuxDesktopEnvironment()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var isRootLaunch = string.Equals(Environment.GetEnvironmentVariable("USER"), "root", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.UserName, "root", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("LOGNAME"), "root", StringComparison.OrdinalIgnoreCase);

        if (!isRootLaunch)
        {
            return;
        }

        var dbusAddress = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (!string.IsNullOrWhiteSpace(dbusAddress))
        {
            return;
        }

        var sudoUid = Environment.GetEnvironmentVariable("SUDO_UID");
        if (!string.IsNullOrWhiteSpace(sudoUid))
        {
            var runtimeDir = Path.Combine("/run/user", sudoUid);
            var busPath = Path.Combine(runtimeDir, "bus");
            if (File.Exists(busPath))
            {
                Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", $"unix:path={busPath}");

                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")) && Directory.Exists(runtimeDir))
                {
                    Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", runtimeDir);
                }

                Console.Error.WriteLine("OmenCore: recovered missing DBus session address from invoking user runtime (sudo launch detected).");
                return;
            }
        }

        // Prevent accessibility bus initialization from hard-failing startup in root/no-session contexts.
        Environment.SetEnvironmentVariable("NO_AT_BRIDGE", "1");
        Console.Error.WriteLine("OmenCore: no session DBus detected under root launch; accessibility bridge disabled. Prefer launching GUI without sudo.");
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
                $"user={Environment.UserName}",
                $"sudo_uid={Environment.GetEnvironmentVariable("SUDO_UID") ?? string.Empty}",
                $"session_type={Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? string.Empty}",
                $"display={Environment.GetEnvironmentVariable("DISPLAY") ?? string.Empty}",
                $"wayland_display={Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? string.Empty}",
                $"xdg_runtime_dir={Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? string.Empty}",
                $"dbus_session_bus_address={Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") ?? string.Empty}",
                $"render_mode_override={Environment.GetEnvironmentVariable(RenderModeEnvVar) ?? string.Empty}",
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

    private sealed class RenderStartupState
    {
        public string? LastKnownGoodRenderMode { get; set; }
        public int ConsecutiveRendererStartupFailures { get; set; }
        public DateTimeOffset? LastFailureUtc { get; set; }
    }
}
