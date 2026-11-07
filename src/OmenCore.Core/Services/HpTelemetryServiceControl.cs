using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace OmenCore.Services
{
    /// <summary>
    /// Reversible switch for HP's analytics/diagnostics background services. Unlike the OMEN Gaming Hub
    /// cleanup (which deletes services), this stops each service and sets it to Disabled after recording its
    /// original start type, so "Restore" puts the machine back exactly as it was. Services that were already
    /// disabled are left alone and never "restored" to enabled.
    /// </summary>
    public sealed class HpTelemetryServiceControl
    {
        // Telemetry and diagnostics only. Nothing OmenCore itself depends on (no WMI/BIOS or driver services).
        public static readonly string[] TelemetryServices =
        {
            "HpTouchpointAnalyticsService", // HP Analytics
            "HPDiagsCap",                   // HP Diagnostics
            "HPAppHelperCap",               // HP App Helper
            "HPSysInfoCap",                 // HP System Info
            "HPNetworkCap",                 // HP Network telemetry
        };

        public const int Disabled = 4;
        public const int DelayedAuto = 5; // our own code: Start=2 plus DelayedAutostart=1

        public interface IServiceBackend
        {
            /// <summary>Start type (2 auto, 3 manual, 4 disabled, 5 delayed auto), or null if the service is absent.</summary>
            int? GetStartType(string name);
            bool SetStartType(string name, int startType);
            void Stop(string name);
        }

        public sealed record ServiceState(string Name, int? StartType, bool Managed);

        public sealed record Result(int Changed, int Failed, int Skipped)
        {
            public string Describe(string verb) =>
                Failed > 0 ? $"{verb} {Changed} service(s); {Failed} failed (run as administrator)."
                : Changed == 0 ? "Nothing to change." : $"{verb} {Changed} service(s).";
        }

        private readonly IServiceBackend _backend;
        private readonly string _backupPath;
        private readonly Action<string>? _log;

        public HpTelemetryServiceControl(string backupPath, IServiceBackend? backend = null, Action<string>? log = null)
        {
            _backupPath = backupPath;
            _backend = backend ?? new ScBackend();
            _log = log;
        }

        public IReadOnlyList<ServiceState> Query()
        {
            var saved = LoadBackup();
            return TelemetryServices
                .Select(n => new ServiceState(n, _backend.GetStartType(n), saved.ContainsKey(n)))
                .Where(s => s.StartType.HasValue)
                .ToList();
        }

        public Result DisableAll()
        {
            var saved = LoadBackup();
            int changed = 0, failed = 0, skipped = 0;
            foreach (var name in TelemetryServices)
            {
                var current = _backend.GetStartType(name);
                if (current is null || current == Disabled) { skipped++; continue; }

                // Record the original BEFORE touching anything so a crash cannot lose it.
                if (!saved.ContainsKey(name)) { saved[name] = current.Value; SaveBackup(saved); }

                if (_backend.SetStartType(name, Disabled))
                {
                    try { _backend.Stop(name); } catch (Exception ex) { _log?.Invoke($"Stop {name} failed: {ex.Message}"); }
                    changed++;
                }
                else
                {
                    failed++;
                    if (_backend.GetStartType(name) == saved[name]) { saved.Remove(name); SaveBackup(saved); }
                }
            }
            return new Result(changed, failed, skipped);
        }

        public Result RestoreAll()
        {
            var saved = LoadBackup();
            int changed = 0, failed = 0;
            foreach (var (name, original) in saved.ToList())
            {
                if (_backend.GetStartType(name) is null) { saved.Remove(name); continue; }
                if (_backend.SetStartType(name, original)) { saved.Remove(name); changed++; }
                else failed++;
                SaveBackup(saved);
            }
            return new Result(changed, failed, 0);
        }

        private Dictionary<string, int> LoadBackup()
        {
            try
            {
                if (File.Exists(_backupPath))
                    return JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(_backupPath)) ?? new();
            }
            catch (Exception ex) { _log?.Invoke($"HP service backup unreadable: {ex.Message}"); }
            return new();
        }

        private void SaveBackup(Dictionary<string, int> saved)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_backupPath)!);
            if (saved.Count == 0) { if (File.Exists(_backupPath)) File.Delete(_backupPath); return; }
            File.WriteAllText(_backupPath, JsonSerializer.Serialize(saved));
        }

        private sealed class ScBackend : IServiceBackend
        {
            public int? GetStartType(string name)
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
                if (key?.GetValue("Start") is not int start) return null;
                return start == 2 && key.GetValue("DelayedAutostart") is int d && d == 1 ? DelayedAuto : start;
            }

            public bool SetStartType(string name, int startType)
            {
                var mode = startType switch { 2 => "auto", 3 => "demand", 4 => "disabled", DelayedAuto => "delayed-auto", _ => null };
                return mode != null && Sc($"config {name} start= {mode}") == 0;
            }

            public void Stop(string name) => Sc($"stop {name}");

            private static int Sc(string args)
            {
                using var p = Process.Start(new ProcessStartInfo("sc.exe", args)
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
                if (p == null) return -1;
                _ = p.StandardOutput.ReadToEndAsync(); // drain without blocking the timeout below
                if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } return -1; }
                return p.ExitCode;
            }
        }
    }
}
