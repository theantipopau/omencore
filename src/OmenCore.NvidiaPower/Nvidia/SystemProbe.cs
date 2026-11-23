using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NvpwrControlBlackwell
{
    internal static class SystemProbe
    {
        public static string RunProcess(string file, string args, int timeoutMs, out int exitCode)
        {
            return RunProcess(file, args, timeoutMs, true, out exitCode);
        }

        public static string RunProcess(string file, string args, int timeoutMs, bool createNoWindow, out int exitCode)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = file;
            psi.Arguments = args;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = createNoWindow;
            if (!createNoWindow)
                psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (Process p = Process.Start(psi))
            {
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    exitCode = -999;
                    return "Process timed out: " + file;
                }
                exitCode = p.ExitCode;
                return stdoutTask.GetAwaiter().GetResult() + stderrTask.GetAwaiter().GetResult();
            }
        }

        public static string RunNvidiaSmi(string args)
        {
            int rc;
            string s = RunProcess("nvidia-smi.exe", args, 15000, out rc);
            if (rc != 0) throw new InvalidOperationException("nvidia-smi failed: " + s.Trim());
            return s;
        }

        public static string QueryOne(string field)
        {
            string s = RunNvidiaSmi("--query-gpu=" + field + " --format=csv,noheader");
            string[] lines = s.Replace("\r", "").Split('\n');
            var nonempty = Array.FindAll(lines, line => !String.IsNullOrWhiteSpace(line));
            if (nonempty.Length != 1) throw new InvalidOperationException("Exactly one NVIDIA GPU is required; refusing ambiguous adapter selection.");
            return nonempty[0].Trim();
        }

        public static string GetGpuName()
        {
            try
            {
                string name, vbios, error;
                uint deviceId, subsystemId;
                if (NvApiTelemetry.TryGetIdentity(
                    out name,
                    out vbios,
                    out deviceId,
                    out subsystemId,
                    out error) &&
                    !String.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch { }

            return QueryOne("name");
        }

        public static string GetDriverVersion()
        {
            return QueryOne("driver_version");
        }

        public static string GetVbios()
        {
            try
            {
                string name, vbios, error;
                uint deviceId, subsystemId;
                if (NvApiTelemetry.TryGetIdentity(
                    out name,
                    out vbios,
                    out deviceId,
                    out subsystemId,
                    out error) &&
                    !String.IsNullOrWhiteSpace(vbios))
                    return vbios;
            }
            catch { }

            return QueryOne("vbios_version");
        }

        public static PowerState GetPowerState()
        {
            string raw = RunNvidiaSmi("-q -d POWER");
            PowerState s = new PowerState();
            s.Raw = raw;
            s.CurrentW = ParsePowerField(raw, "Current Power Limit");
            s.RequestedW = ParsePowerField(raw, "Requested Power Limit");
            s.MaxW = ParsePowerField(raw, "Max Power Limit");
            return s;
        }

        private static double? ParsePowerField(string text, string field)
        {
            Match m = Regex.Match(text,
                "(?m)^\\s*" + Regex.Escape(field) + "\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)\\s*W\\s*$");
            if (!m.Success) return null;
            double v;
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out v)) return v;
            return null;
        }

        public static TelemetryState GetTelemetry()
        {
            TelemetryState t = new TelemetryState();
            StringBuilder errors = new StringBuilder();

            try
            {
                string nvmlError;
                if (!NvmlTelemetry.Fill(t, out nvmlError) &&
                    !String.IsNullOrEmpty(nvmlError))
                    errors.Append("NVML: ").Append(nvmlError).Append("; ");
            }
            catch (Exception ex)
            {
                errors.Append("NVML: ").Append(SafeError.Describe(ex)).Append("; ");
            }

            try
            {
                NvApiTelemetry.Fill(t);
            }
            catch (Exception ex)
            {
                errors.Append("NVAPI: ").Append(SafeError.Describe(ex)).Append("; ");
            }

            // Per-field fallback. An unsupported voltage/memory-temperature
            // query must not make power/clocks/temperature disappear.
            if (!t.PowerW.HasValue)
                t.PowerW = TrySmiNumber("power.draw");

            if (!t.GpuTempC.HasValue)
                t.GpuTempC = TrySmiNumber("temperature.gpu");

            if (!t.MemoryTempC.HasValue)
                t.MemoryTempC = TrySmiNumber("temperature.memory");

            if (!t.UtilizationPct.HasValue)
                t.UtilizationPct = TrySmiNumber("utilization.gpu");

            if (!t.CoreClockMHz.HasValue)
                t.CoreClockMHz = TrySmiNumber("clocks.gr");

            if (!t.MemoryClockMHz.HasValue)
                t.MemoryClockMHz = TrySmiNumber("clocks.mem");

            if (!t.VoltageV.HasValue)
            {
                double? v = TrySmiNumber("voltage.graphics");
                if (v.HasValue)
                    t.VoltageV = v.Value > 10.0 ? v.Value / 1000.0 : v.Value;
            }

            if (t.Power == null)
                t.Power = new PowerState();

            // NVML usually fills CURRENT/MAX without spawning nvidia-smi.
            // Keep the established public POWER parser as a fallback.
            if (!t.Power.CurrentW.HasValue ||
                !t.Power.MaxW.HasValue)
            {
                try
                {
                    PowerState p = GetPowerState();

                    if (!t.Power.CurrentW.HasValue)
                        t.Power.CurrentW = p.CurrentW;

                    if (!t.Power.RequestedW.HasValue)
                        t.Power.RequestedW = p.RequestedW;

                    if (!t.Power.MaxW.HasValue)
                        t.Power.MaxW = p.MaxW;

                    t.Power.Raw = p.Raw;
                }
                catch (Exception ex)
                {
                    errors.Append("POWER: ").Append(SafeError.Describe(ex)).Append("; ");
                }
            }

            t.Error = errors.ToString().Trim();
            return t;
        }

        private static double? TrySmiNumber(string field)
        {
            try
            {
                string s = RunNvidiaSmi(
                    "--query-gpu=" + field +
                    " --format=csv,noheader,nounits");

                string[] lines = s.Replace("\r", "").Split('\n');
                if (lines.Length == 0)
                    return null;

                return ParseNumber(lines[0]);
            }
            catch
            {
                return null;
            }
        }

        private static double? ParseNumber(string s)
        {
            if (s == null) return null;
            s = s.Trim();
            if (s.Equals("N/A", StringComparison.OrdinalIgnoreCase) || s.Length == 0) return null;
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return null;
        }

        public static string Sha256File(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
            {
                byte[] h = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string ResolveNvlddmkmPath()
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT PathName FROM Win32_SystemDriver WHERE Name='nvlddmkm'"))
            {
                foreach (ManagementObject o in searcher.Get())
                {
                    string p = Convert.ToString(o["PathName"]);
                    if (String.IsNullOrEmpty(p)) continue;
                    p = p.Trim();
                    if (p.StartsWith("\""))
                    {
                        int end = p.IndexOf('"', 1);
                        if (end > 1) p = p.Substring(1, end - 1);
                    }
                    else
                    {
                        int sp = p.IndexOf(' ');
                        if (sp > 0) p = p.Substring(0, sp);
                    }
                    if (p.StartsWith("\\??\\")) p = p.Substring(4);
                    if (p.StartsWith("\\SystemRoot\\", StringComparison.OrdinalIgnoreCase))
                        p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), p.Substring(12));
                    if (File.Exists(p)) return Path.GetFullPath(p);
                }
            }
            throw new FileNotFoundException("nvlddmkm.sys path was not resolved.");
        }

        public static string FindNvidiaDeviceInstanceId(string expectedName)
        {
            string selected = null;
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT Name,DeviceID FROM Win32_PnPEntity WHERE PNPClass='Display' AND Present=True"))
            {
                foreach (ManagementObject o in searcher.Get())
                {
                    string name = Convert.ToString(o["Name"]);
                    string id = Convert.ToString(o["DeviceID"]);
                    if (!String.IsNullOrEmpty(name) &&
                        name.Equals(expectedName, StringComparison.OrdinalIgnoreCase) &&
                        !String.IsNullOrEmpty(id)) {
                        if (selected != null) throw new InvalidOperationException("More than one matching NVIDIA adapter is present.");
                        selected = id;
                    }
                }
            }
            return selected;
        }

        public static OperationResult RestartNvidiaDevice(string expectedName)
        {
            string id = FindNvidiaDeviceInstanceId(expectedName);
            if (String.IsNullOrEmpty(id)) return OperationResult.Fail("NVIDIA PnP device was not found.");
            int rc;
            string result = RunProcess("pnputil.exe", "/restart-device \"" + id + "\"", 30000, out rc);
            if (rc != 0) return OperationResult.Fail("pnputil restart failed: " + result.Trim());
            return OperationResult.Ok("NVIDIA device restart requested.");
        }

        public static OperationResult RebootWindowsNow()
        {
            int rc;
            string result = RunProcess("shutdown.exe", "/r /t 0", 10000, out rc);
            if (rc != 0) return OperationResult.Fail("Windows reboot failed: " + result.Trim());
            return OperationResult.Ok("Windows reboot requested.");
        }
    }
}
