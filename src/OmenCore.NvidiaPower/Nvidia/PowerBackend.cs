using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NvpwrControlBlackwell
{
    internal sealed class PowerBackend
    {
        public const string Version = "1.2.9-omencore"; // backend ported from Prophecy Power Unlocker 1.2.9
        public const uint ApiRmControl = 0x07000046;
        public const uint CmdE633 = 0x2080E633;
        public const int PacketSize = 0x122D70;

        private const string DisplayClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";
        private const string TaskName = "OmenCoreProphecy";
        private const uint A630Qi = 0x54924BF5;
        private const uint A630Version = 0x00010728;
        private const int A630Size = 0x728;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InternalTransportDelegate(
            uint api,
            IntPtr packet,
            uint packetSize,
            uint arg4,
            uint arg5,
            uint arg6,
            uint arg7);

        /// <summary>
        /// Runs the actual CURRENT write. The host replaces this to prepare the laptop first (on Victus: HP performance
        /// mode, fan engine paused). Default is a plain call.
        /// </summary>
        public static Func<Func<int>, int> CurrentWriteGuard { get; set; } = write => write();

        public CompatibilityState CheckCompatibility()
        {
            CompatibilityState s = new CompatibilityState();
            try
            {
                s.GpuName = SystemProbe.GetGpuName();
                s.GpuPnpId = SystemProbe.FindNvidiaDeviceInstanceId(s.GpuName) ?? "";
                s.Profile = GpuProfiles.Detect(s.GpuName, s.GpuPnpId);
                s.Identity = GpuProfiles.BuildIdentity(s.GpuName, s.GpuPnpId, s.Profile);
                s.Vbios = SystemProbe.GetVbios();
                s.DriverVersion = SystemProbe.GetDriverVersion();
                s.KmdPath = SystemProbe.ResolveNvlddmkmPath();
                s.KmdSha256 = SystemProbe.Sha256File(s.KmdPath);

                using (NvApiSession nv = new NvApiSession())
                {
                    string err;
                    if (!nv.Open(out err))
                    {
                        s.Reason = err;
                        return s;
                    }
                    s.ImplPath = nv.ImplPath;
                    s.ImplSha256 = nv.ImplHash;
                    s.ImplBase = nv.ImplBase.ToInt64();
                }

                if (s.Profile == null)
                {
                    s.Reason = "Unsupported GPU. Supported family: RTX 4050/4060/4070/4080/4090 and RTX 5050/5060/5070/5070 Ti/5080/5090 Laptop.";
                    return s;
                }

                s.Driver = DriverResolver.Resolve(s.ImplPath, s.ImplSha256, s.KmdSha256, s.GpuPnpId);
                s.VbiosResolver = VbiosResolver.ResolveKnownOrCache(s.Profile, s.Vbios, s.GpuPnpId);
                s.CurrentWritesReady = s.Driver != null && s.Driver.Trusted;
                s.MaxWritesReady = s.VbiosResolver != null && s.VbiosResolver.Resolved;
                s.Policy = AssessPolicy(s);
                if (s.Policy != null && !s.Policy.Consistent)
                {
                    s.CurrentWritesReady = false;
                    s.MaxWritesReady = false;
                }
                s.Supported = s.CurrentWritesReady && s.MaxWritesReady;

                List<string> parts = new List<string>();
                parts.Add("GPU profile: " + s.Profile.ToString());
                parts.Add("driver: " + (s.Driver != null ? s.Driver.Reason : "unresolved"));
                parts.Add("VBIOS: " + (s.VbiosResolver != null ? s.VbiosResolver.Reason : "unresolved"));
                parts.Add("policy: " + (s.Policy != null ? s.Policy.State + " — " + s.Policy.Reason : "unresolved"));
                s.Reason = String.Join(" | ", parts.ToArray());
            }
            catch (Exception ex)
            {
                s.Supported = false;
                s.CurrentWritesReady = false;
                s.MaxWritesReady = false;
                s.Reason = SafeError.Describe(ex);
            }
            return s;
        }

        public GpuProfile GetProfile()
        {
            try
            {
                string name = SystemProbe.GetGpuName();
                string pnp = SystemProbe.FindNvidiaDeviceInstanceId(name) ?? "";
                return GpuProfiles.Detect(name, pnp);
            }
            catch { return null; }
        }

        public int[] GetTargets()
        {
            GpuProfile p = GetProfile();
            return p != null ? p.Targets() : new int[0];
        }

        public PowerState GetPowerState()
        {
            return SystemProbe.GetPowerState();
        }

        public OperationResult ValidateDriverResolver()
        {
            CompatibilityState c = CheckCompatibility();
            if (c.Profile == null) return OperationResult.Fail("GPU detection failed: " + c.Reason);
            if (c.Driver == null || !c.Driver.CandidateFound)
                return OperationResult.Fail("No unique NVIDIA RM transport candidate was found. " + (c.Driver != null ? c.Driver.Reason : ""));
            if (c.Driver.Trusted)
                return OperationResult.Ok("Driver transport is already trusted: " + c.Driver.Source + ".");

            TelemetryState tele = SystemProbe.GetTelemetry();
            if (tele.UtilizationPct.HasValue && tele.UtilizationPct.Value > 20.0)
                return OperationResult.Fail("Close GPU workloads before driver validation. GPU utilization is " + tele.UtilizationPct.Value.ToString("0") + "%.");

            PowerState before = GetPowerState();
            if (!before.CurrentW.HasValue || !before.MaxW.HasValue)
                return OperationResult.Fail("CURRENT/MAX public readback is unavailable; no-op validation cannot run.");

            // 1.2.9: the no-op replays the driver's retained F7 source request, never the public ceiling (which can
            // read 140 W over a 115 W request, so replaying it would change power in a step meant to change nothing).
            int currentMilliwatts;
            try { currentMilliwatts = DriverValidationGuard.NoopTarget(F7ContributionReadback.Read(), before.MaxW); }
            catch (InvalidOperationException ex) { return OperationResult.Fail(SafeError.Describe(ex)); }

            using (NvApiSession nv = new NvApiSession())
            {
                string err;
                if (!nv.Open(out err)) return OperationResult.Fail(err);
                if (!nv.ImplHash.Equals(c.ImplSha256, StringComparison.OrdinalIgnoreCase))
                    return OperationResult.Fail("Loaded nvapi64_impl.dll changed during validation.");

                string a630Reason;
                if (!ValidateA630(nv, before, out a630Reason))
                    return OperationResult.Fail("A630 Board Power validation failed: " + a630Reason);

                uint completion;
                int tr;
                OperationResult send = SendE633(nv, c.Driver.TransportRva, currentMilliwatts, out tr, out completion);
                if (!send.Success) return OperationResult.Fail("E633 CURRENT no-op validation failed: " + send.Message);
            }

            System.Threading.Thread.Sleep(700);
            PowerState after = GetPowerState();
            uint? sourceAfter = F7ContributionReadback.Read();
            if (!after.MaxW.HasValue || Math.Abs(after.MaxW.Value - before.MaxW.Value) > 0.01 ||
                !sourceAfter.HasValue || sourceAfter.Value != (uint)currentMilliwatts)
                return OperationResult.Fail("The no-op could not confirm the driver's request and NVIDIA MAX were unchanged; writes stay locked. No rollback request was inferred from the public ceiling.");

            DriverResolver.MarkTrusted(c.Driver);
            AppLog.Write("Driver resolver validated transport RVA 0x" + c.Driver.TransportRva.ToString("X") + " for impl " + c.ImplSha256);
            return OperationResult.Ok("New NVIDIA driver validated: the no-op kept the driver's request and NVIDIA MAX unchanged. Future launches trust this exact driver. Loaded power is not verified.");
        }

        public OperationResult ResolveVbiosFromRom(string romPath)
        {
            CompatibilityState c = CheckCompatibility();
            if (c.Profile == null) return OperationResult.Fail("GPU detection failed: " + c.Reason);
            VbiosResolution r = VbiosResolver.ResolveFromRom(romPath, c.Profile, c.Vbios, c.GpuPnpId);
            if (!r.Resolved) return OperationResult.Fail(r.Reason);
            return OperationResult.Ok(
                "VBIOS resolved. Stock MAX=" + r.StockMaxW.ToString() + " W; shadow MAX offset=0x" +
                r.ShadowOffset.ToString("X") + "; Power Budget table raw=0x" + r.PowerTableRawOffset.ToString("X") + ".");
        }

        public OperationResult TryAutoResolveVbios(out string dumpedRom)
        {
            dumpedRom = "";
            CompatibilityState c = CheckCompatibility();
            if (c.Profile == null) return OperationResult.Fail("GPU detection failed: " + c.Reason);
            if (c.VbiosResolver != null && c.VbiosResolver.Resolved)
                return OperationResult.Ok("VBIOS resolver is already ready: " + c.VbiosResolver.Source + ".");

            string msg;
            if (!VbiosResolver.TryAutoDumpRom(out dumpedRom, out msg))
                return OperationResult.Fail(msg);

            VbiosResolution parsed = VbiosResolver.ResolveFromRom(dumpedRom, c.Profile, c.Vbios, c.GpuPnpId);
            OperationResult resolved = parsed.Resolved ? OperationResult.Ok(parsed.Reason) : OperationResult.Fail(parsed.Reason);
            if (!resolved.Success) return resolved;
            resolved.Message = msg + Environment.NewLine + resolved.Message;
            return resolved;
        }

        public int? GetInstalledMaxOverrideTarget()
        {
            CompatibilityState c = CheckCompatibility();
            return ReadInstalledMaxOverrideTarget(c);
        }

        private int? ReadInstalledMaxOverrideTarget(CompatibilityState c)
        {
            if (c == null || c.Profile == null || c.VbiosResolver == null || !c.VbiosResolver.Resolved) return null;

            string native;
            RegistryKey key = OpenNvidiaDriverKey(false, c.GpuPnpId, out native);
            if (key == null) return null;
            using (key)
            {
                byte[] data = key.GetValue("romOverride00", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as byte[];
                if (data == null) return null;
                int stockW = (c.VbiosResolver != null && c.VbiosResolver.StockMaxW > 0) ? c.VbiosResolver.StockMaxW : c.Profile.StockPowerW;
                foreach (int w in c.Profile.Targets())
                {
                    if (w == stockW) continue;
                    if (ByteArrayEqual(data, BuildOverride(w, c.VbiosResolver.ShadowOffset))) return w;
                }
                return -1;
            }
        }

        private PowerPolicyAssessment AssessPolicy(CompatibilityState c)
        {
            PowerPolicyAssessment a = new PowerPolicyAssessment();
            if (c == null || c.Profile == null)
            {
                a.State = "UNRESOLVED";
                a.Reason = "GPU profile is unavailable.";
                a.Consistent = false;
                return a;
            }

            PowerState live = null;
            try { live = GetPowerState(); } catch { }
            if (live == null || !live.CurrentW.HasValue || !live.MaxW.HasValue)
            {
                a.State = "READBACK_UNAVAILABLE";
                a.Reason = "Public NVIDIA CURRENT/MAX readback is unavailable.";
                a.Consistent = false;
                return a;
            }

            a.LiveCurrentW = live.CurrentW;
            a.LiveMaxW = live.MaxW;
            int? ov = null;
            try { ov = ReadInstalledMaxOverrideTarget(c); } catch { }
            a.InstalledOverrideW = ov;

            double cur = live.CurrentW.Value;
            double max = live.MaxW.Value;
            int stockW = (c.VbiosResolver != null && c.VbiosResolver.StockMaxW > 0) ? c.VbiosResolver.StockMaxW : c.Profile.StockPowerW;
            if (cur > max + 0.51)
            {
                a.State = "INCONSISTENT";
                a.Reason = "CURRENT exceeds live MAX.";
                a.Consistent = false;
                return a;
            }
            if (ov.HasValue && ov.Value == -1)
            {
                a.State = "FOREIGN_OVERRIDE";
                a.Reason = "romOverride00 exists but is not recognized as this tool's value for the resolved VBIOS.";
                a.Consistent = false;
                return a;
            }
            if (ov.HasValue && ov.Value > c.Profile.MaxPowerW)
            {
                a.State = "INCONSISTENT";
                a.Reason = "Installed MAX override is outside the detected GPU profile range.";
                a.Consistent = false;
                return a;
            }

            if (ov.HasValue && ov.Value > 0 && Math.Abs(max - ov.Value) > 0.51)
            {
                a.State = "MAX_PENDING_REBOOT";
                a.Reason = "Tool MAX override is installed but the live NVIDIA policy has not rebuilt to that value yet.";
                a.Consistent = true;
                return a;
            }

            if (!ov.HasValue && Math.Abs(max - stockW) <= 0.51 && Math.Abs(cur - stockW) <= 0.51)
            {
                a.State = "STOCK";
                a.Reason = "Live CURRENT/MAX match the detected stock profile.";
                a.Consistent = true;
                return a;
            }

            if (ov.HasValue && ov.Value > 0 && Math.Abs(max - ov.Value) <= 0.51 && Math.Abs(cur - stockW) <= 0.51)
            {
                a.State = "MAX_ACTIVE_CURRENT_STOCK";
                a.Reason = "MAX override is active; CURRENT remains at the stock value.";
                a.Consistent = true;
                return a;
            }

            if (Math.Abs(cur - max) <= 0.51)
            {
                a.State = "TARGET_ACTIVE";
                a.Reason = "CURRENT and MAX are aligned.";
                a.Consistent = true;
                return a;
            }

            if (!ov.HasValue && Math.Abs(max - stockW) > 0.51)
            {
                a.State = "EXTERNAL_MAX_STATE";
                a.Reason = "Live MAX differs from the nominal profile and no tool-owned override is installed. This may be an OEM/external policy.";
                a.Consistent = true;
                return a;
            }

            a.State = "CUSTOM_CONSISTENT";
            a.Reason = "CURRENT/MAX form a valid ordered policy state.";
            a.Consistent = true;
            return a;
        }

        public OperationResult SetMaxOverride(int watts)
        {
            CompatibilityState c = CheckCompatibility();
            if (c.Profile == null) return OperationResult.Fail("Unsupported GPU profile.");
            if (!c.Profile.IsInRange(watts)) return OperationResult.Fail("MAX target is outside this GPU profile range.");
            int stockW = (c.VbiosResolver != null && c.VbiosResolver.StockMaxW > 0) ? c.VbiosResolver.StockMaxW : c.Profile.StockPowerW;
            if (watts == stockW) return RemoveMaxOverride();
            if (!c.MaxWritesReady)
                return OperationResult.Fail("VBIOS MAX resolver is not ready. Resolve/dump the VBIOS first. " + (c.VbiosResolver != null ? c.VbiosResolver.Reason : ""));

            try
            {
                PowerState liveBeforeMax = GetPowerState();
                if (liveBeforeMax.CurrentW.HasValue && liveBeforeMax.CurrentW.Value > watts + 0.01)
                    return OperationResult.Fail("CURRENT is " + liveBeforeMax.CurrentW.Value.ToString("0.00", CultureInfo.InvariantCulture) + " W. Restore/lower CURRENT before selecting a lower future MAX of " + watts.ToString() + " W.");
            }
            catch { }

            string native;
            RegistryKey key = OpenNvidiaDriverKey(true, c.GpuPnpId, out native);
            if (key == null) return OperationResult.Fail("NVIDIA driver registry key not found.");

            using (key)
            {
                byte[] previous = null;
                foreach (string name in key.GetValueNames())
                {
                    if (!name.StartsWith("romOverride", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!name.Equals("romOverride00", StringComparison.OrdinalIgnoreCase))
                        return OperationResult.Fail("Additional romOverride values are present. Refusing to overwrite them.");
                    previous = key.GetValue(name) as byte[];
                    if (previous != null && !IsToolOverride(previous, c.Profile, c.VbiosResolver.ShadowOffset))
                        return OperationResult.Fail("Existing romOverride00 is not owned by this tool/current VBIOS resolver.");
                }

                string backupError;
                if (!BackupRegistry(native, "before-max" + watts.ToString(CultureInfo.InvariantCulture), out backupError))
                    return OperationResult.Fail("Registry backup failed: " + backupError);

                byte[] blob = BuildOverride(watts, c.VbiosResolver.ShadowOffset);
                key.SetValue("romOverride00", blob, RegistryValueKind.Binary);
                byte[] readback = key.GetValue("romOverride00") as byte[];
                if (readback == null || !ByteArrayEqual(blob, readback))
                {
                    try
                    {
                        if (previous != null) key.SetValue("romOverride00", previous, RegistryValueKind.Binary);
                        else key.DeleteValue("romOverride00", false);
                    }
                    catch { }
                    AppLog.Write("MAX transaction rollback after romOverride readback mismatch.");
                    return OperationResult.Fail("romOverride00 readback mismatch. Previous registry state was restored when possible.");
                }
            }

            AppLog.Write("Installed MAX override " + watts.ToString() + " W GPU=" + c.GpuName + " shadow=0x" + c.VbiosResolver.ShadowOffset.ToString("X"));
            OperationResult ok = OperationResult.Ok(
                "MAX " + watts.ToString() + " W was saved to NVIDIA romOverride. Reboot is required. After reboot reopen Prophecy Power Unlocker, verify MAX, then apply CURRENT.");
            ok.RebootRequired = true;
            return ok;
        }

        public OperationResult RemoveMaxOverride()
        {
            CompatibilityState c = CheckCompatibility();
            if (c.Profile == null) return OperationResult.Fail("Unsupported GPU profile.");
            if (c.VbiosResolver == null || !c.VbiosResolver.Resolved)
                return OperationResult.Fail("VBIOS resolver is not ready; cannot safely identify tool-owned romOverride00.");

            try
            {
                int stockW = (c.VbiosResolver != null && c.VbiosResolver.StockMaxW > 0) ? c.VbiosResolver.StockMaxW : c.Profile.StockPowerW;
                PowerState live = GetPowerState();
                if (live.CurrentW.HasValue && live.CurrentW.Value > stockW + 0.01)
                    return OperationResult.Fail("Restore CURRENT to stock " + stockW.ToString() + " W while GPU is idle before removing MAX override.");
            }
            catch { }

            string native;
            RegistryKey key = OpenNvidiaDriverKey(true, c.GpuPnpId, out native);
            if (key == null) return OperationResult.Fail("NVIDIA driver registry key not found.");

            using (key)
            {
                List<string> overrides = new List<string>();
                foreach (string n in key.GetValueNames())
                    if (n.StartsWith("romOverride", StringComparison.OrdinalIgnoreCase)) overrides.Add(n);

                if (overrides.Count == 0)
                    return OperationResult.Ok("No MAX override is installed.");

                if (overrides.Count != 1 || !overrides[0].Equals("romOverride00", StringComparison.OrdinalIgnoreCase))
                    return OperationResult.Fail("Foreign/additional romOverride values are present. Refusing to remove them.");

                byte[] old = key.GetValue("romOverride00") as byte[];
                if (old == null || !IsToolOverride(old, c.Profile, c.VbiosResolver.ShadowOffset))
                    return OperationResult.Fail("romOverride00 is not recognized as this tool's value for the resolved VBIOS.");

                string backupError;
                if (!BackupRegistry(native, "before-remove-max", out backupError))
                    return OperationResult.Fail("Registry backup failed: " + backupError);

                key.DeleteValue("romOverride00", false);
                if (key.GetValue("romOverride00", null, RegistryValueOptions.DoNotExpandEnvironmentNames) != null)
                {
                    try { key.SetValue("romOverride00", old, RegistryValueKind.Binary); } catch { }
                    return OperationResult.Fail("romOverride00 delete readback failed. Previous value was restored when possible.");
                }
            }

            AppLog.Write("Removed MAX override");
            OperationResult result = OperationResult.Ok(
                "MAX override removed. Reboot Windows to restore the factory MAX. After reboot the NVIDIA policy will be rebuilt from stock VBIOS data.");
            result.RebootRequired = true;
            return result;
        }

        public OperationResult SetCurrent(int watts)
        {
            CompatibilityState c = CheckCompatibility();
            if (c.Profile == null) return OperationResult.Fail("Unsupported GPU profile.");
            int stockW = c.VbiosResolver != null && c.VbiosResolver.StockMaxW > 0 ? c.VbiosResolver.StockMaxW : c.Profile.StockPowerW;
            if (watts != stockW && !c.Profile.IsInRange(watts)) return OperationResult.Fail("CURRENT target is outside this GPU profile range.");
            if (!c.CurrentWritesReady)
                return OperationResult.Fail("Driver transport is not trusted. Validate the new driver first. " + (c.Driver != null ? c.Driver.Reason : ""));

            TelemetryState tele = SystemProbe.GetTelemetry();
            if (tele.UtilizationPct.HasValue && tele.UtilizationPct.Value > 30.0)
                return OperationResult.Fail("Change CURRENT while GPU is idle. Current GPU utilization is " + tele.UtilizationPct.Value.ToString("0") + "%.");

            PowerState p = GetPowerState();
            if (!p.MaxW.HasValue) return OperationResult.Fail("Public NVIDIA Max Power Limit is unavailable.");
            if (watts > (int)Math.Floor(p.MaxW.Value + 0.01))
                return OperationResult.Fail("CURRENT " + watts.ToString() + " W exceeds live MAX " + p.MaxW.Value.ToString("0.00", CultureInfo.InvariantCulture) + " W. Apply MAX first, reboot Windows, then verify the new live MAX before applying CURRENT.");

            int milliwatts = watts * 1000;
            using (NvApiSession nv = new NvApiSession())
            {
                string err;
                if (!nv.Open(out err)) return OperationResult.Fail(err);
                if (!nv.ImplHash.Equals(c.ImplSha256, StringComparison.OrdinalIgnoreCase))
                    return OperationResult.Fail("Loaded nvapi64_impl.dll changed after resolver check.");

                uint completion; int tr;
                OperationResult send = SendE633(nv, c.Driver.TransportRva, milliwatts, out tr, out completion);
                if (!send.Success) return send;
            }

            System.Threading.Thread.Sleep(700);
            PowerState after = GetPowerState();
            // 1.2.9: E633 authors the F7 source request; the public CURRENT ceiling is not a getter for it, so a
            // differing ceiling is not a failed write, and no rollback request is manufactured from it.
            if (!after.MaxW.HasValue)
                return OperationResult.Fail("E633 accepted the request, but NVIDIA MAX readback became unavailable. Recheck before further changes; no rollback target was inferred from telemetry.");

            // Transaction postcondition: changing CURRENT must not mutate live MAX.
            if (after.MaxW.HasValue && p.MaxW.HasValue && Math.Abs(after.MaxW.Value - p.MaxW.Value) > 0.51)
                return OperationResult.Fail("CURRENT was applied, but live MAX changed unexpectedly from " +
                    p.MaxW.Value.ToString("0.00", CultureInfo.InvariantCulture) + " W to " +
                    after.MaxW.Value.ToString("0.00", CultureInfo.InvariantCulture) + " W. Recheck resolver state before further writes.");

            string target = watts.ToString(CultureInfo.InvariantCulture);
            string ceiling = after.CurrentW.HasValue ? after.CurrentW.Value.ToString("0.00", CultureInfo.InvariantCulture) + " W" : "unavailable";
            uint? f7 = F7ContributionReadback.Read();
            if (f7.HasValue && f7.Value == (uint)milliwatts)
                return OperationResult.Ok("CURRENT " + target + " W accepted; the driver's own request reads " + target + " W. Public GPU ceiling " + ceiling + ". Check power under a game load.");
            if (!after.CurrentW.HasValue || Math.Abs(after.CurrentW.Value - watts) > 0.01)
            {
                AppLog.Write("E633 request accepted target=" + target + " W; public ceiling=" + ceiling + "; source F7 verification unavailable; no synthetic rollback.");
                return OperationResult.Ok("CURRENT " + target + " W accepted, but the public GPU ceiling reads " + ceiling + " and the driver's own request could not be read back. Effective power is not confirmed; check it under a game load.");
            }
            return OperationResult.Ok("CURRENT " + target + " W accepted and the public GPU ceiling matches. Loaded power is not confirmed; check it under a game load.");
        }

        public OperationResult InstallAutostart(int watts, string exePath)
        {
            CompatibilityState c = CheckCompatibility();
            if (c.Profile == null || !c.Profile.IsInRange(watts)) return OperationResult.Fail("Invalid autostart target for this GPU.");
            if (!c.CurrentWritesReady) return OperationResult.Fail("Driver transport must be trusted before autostart is enabled.");

            PowerState p = GetPowerState();
            if (!p.MaxW.HasValue || p.MaxW.Value + 0.01 < watts)
                return OperationResult.Fail("Install/apply MAX " + watts.ToString() + " W and reboot before enabling CURRENT autostart.");

            if (String.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return OperationResult.Fail("Application executable path is unavailable for autostart.");

            string stateDir = Path.Combine(StatePaths.Dir);
            Directory.CreateDirectory(stateDir);

            string userName = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            string ps =
                "$ErrorActionPreference='Stop';" +
                "$a=New-ScheduledTaskAction -Execute '" + PsQuote(exePath) + "' -Argument '--apply-current " + watts.ToString(CultureInfo.InvariantCulture) + " --silent';" +
                "$t=New-ScheduledTaskTrigger -AtLogOn;" +
                "$p=New-ScheduledTaskPrincipal -UserId '" + PsQuote(userName) + "' -LogonType Interactive -RunLevel Highest;" +
                "Register-ScheduledTask -TaskName '" + PsQuote(TaskName) + "' -Action $a -Trigger $t -Principal $p -Force | Out-Null;";

            string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(ps));
            int rc;
            string output = SystemProbe.RunProcess(
                "powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                30000,
                out rc);

            if (rc != 0) return OperationResult.Fail("Scheduled Task creation failed: " + output.Trim());

            File.WriteAllText(Path.Combine(stateDir, "autostart-target.txt"), watts.ToString(CultureInfo.InvariantCulture));
            return OperationResult.Ok("Autostart CURRENT=" + watts.ToString() + " W installed. It will be applied after logon only when this exact driver/GPU resolver state and live MAX permit it.");
        }

        public OperationResult RemoveAutostart()
        {
            int rc;
            string output = SystemProbe.RunProcess("schtasks.exe", "/Delete /F /TN \"" + TaskName + "\"", 30000, out rc);
            if (rc != 0 && output.IndexOf("cannot find", StringComparison.OrdinalIgnoreCase) < 0 &&
                output.IndexOf("не удается найти", StringComparison.OrdinalIgnoreCase) < 0 &&
                output.IndexOf("не найден", StringComparison.OrdinalIgnoreCase) < 0)
                return OperationResult.Fail("schtasks delete failed: " + output.Trim());

            try
            {
                string dir = Path.Combine(StatePaths.Dir);
                string f = Path.Combine(dir, "autostart-target.txt");
                if (File.Exists(f)) File.Delete(f);
            }
            catch { }
            return OperationResult.Ok("Autostart removed.");
        }

        public int? GetAutostartTarget()
        {
            try
            {
                string f = Path.Combine(StatePaths.Dir, "autostart-target.txt");
                if (!File.Exists(f)) return null;
                int v;
                if (Int32.TryParse(File.ReadAllText(f).Trim(), out v)) return v;
            }
            catch { }
            return null;
        }

        private static void WriteRegistryOverride(string subKeyPath, string name, object value, RegistryValueKind kind)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.CreateSubKey(subKeyPath))
                {
                    if (k != null) k.SetValue(name, value, kind);
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("WriteRegistryOverride failed for " + subKeyPath + "\\" + name + ": " + SafeError.Describe(ex));
            }
        }

        private static void DeleteRegistryOverride(string subKeyPath, string name)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(subKeyPath, true))
                {
                    if (k != null) k.DeleteValue(name, false);
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("DeleteRegistryOverride failed for " + subKeyPath + "\\" + name + ": " + SafeError.Describe(ex));
            }
        }

        public OperationResult ExportCompatibilityReport(string path)
        {
            CompatibilityState c = CheckCompatibility();
            PowerState p = null;
            try { p = GetPowerState(); } catch { }
            int? ov = null;
            try { ov = GetInstalledMaxOverrideTarget(); } catch { }

            using (StreamWriter w = new StreamWriter(path, false))
            {
                w.WriteLine("Prophecy Power Unlocker " + Version);
                w.WriteLine("Timestamp: " + DateTime.Now.ToString("o"));
                w.WriteLine("GPU: " + c.GpuName);
                w.WriteLine("Profile: " + (c.Profile != null ? c.Profile.ToString() : "unsupported"));
                w.WriteLine("GPU PnP: " + c.GpuPnpId);
                if (c.Identity != null)
                {
                    w.WriteLine("GPU PCI identity: " + c.Identity.PciSummary());
                    w.WriteLine("GPU identity detection: " + (c.Identity.Detection ?? ""));
                }
                w.WriteLine("VBIOS: " + c.Vbios);
                w.WriteLine("Driver: " + c.DriverVersion);
                w.WriteLine("KMD: " + c.KmdPath);
                w.WriteLine("KMD SHA256: " + c.KmdSha256);
                w.WriteLine("Impl: " + c.ImplPath);
                w.WriteLine("Impl SHA256: " + c.ImplSha256);
                w.WriteLine("CURRENT writes ready: " + c.CurrentWritesReady.ToString());
                w.WriteLine("MAX writes ready: " + c.MaxWritesReady.ToString());
                w.WriteLine("Driver resolver: " + (c.Driver != null ? c.Driver.Reason : "N/A"));
                if (c.Driver != null) w.WriteLine("Transport RVA: 0x" + c.Driver.TransportRva.ToString("X"));
                w.WriteLine("VBIOS resolver: " + (c.VbiosResolver != null ? c.VbiosResolver.Reason : "N/A"));
                if (c.VbiosResolver != null)
                {
                    w.WriteLine("ROM shadow MAX offset: 0x" + c.VbiosResolver.ShadowOffset.ToString("X"));
                    w.WriteLine("Power table raw: 0x" + c.VbiosResolver.PowerTableRawOffset.ToString("X"));
                    w.WriteLine("MAX field raw: 0x" + c.VbiosResolver.MaxFieldRawOffset.ToString("X"));
                    w.WriteLine("Legacy image raw: 0x" + c.VbiosResolver.LegacyImageRawOffset.ToString("X"));
                    w.WriteLine("PCIR device: 0x" + c.VbiosResolver.PcirDeviceId.ToString("X4"));
                    w.WriteLine("Legacy images: " + c.VbiosResolver.RomImageCount.ToString() + "; device-matched: " + c.VbiosResolver.MatchingRomImageCount.ToString());
                    w.WriteLine("Power candidates: " + c.VbiosResolver.CandidateCount.ToString() + "; entry index: " + c.VbiosResolver.EntryIndex.ToString());
                    w.WriteLine("Layout: " + (c.VbiosResolver.Layout ?? ""));
                    w.WriteLine("Power record mW: min=" + c.VbiosResolver.RecordMinMw.ToString() +
                                "; default=" + c.VbiosResolver.RecordDefaultMw.ToString() +
                                "; max=" + c.VbiosResolver.RecordMaxMw.ToString() +
                                "; base=" + c.VbiosResolver.RecordBaseMw.ToString());
                    w.WriteLine("Semantic score: " + c.VbiosResolver.SemanticScore.ToString() + "/100; confidence=" + (c.VbiosResolver.Confidence ?? ""));
                }
                if (c.Policy != null)
                {
                    w.WriteLine("Policy state: " + c.Policy.State);
                    w.WriteLine("Policy consistent: " + c.Policy.Consistent.ToString());
                    w.WriteLine("Policy reason: " + c.Policy.Reason);
                }
                w.WriteLine("Installed tool MAX override: " + (ov.HasValue ? ov.Value.ToString() : "none/unresolved"));
                if (p != null)
                {
                    w.WriteLine("Current W: " + (p.CurrentW.HasValue ? p.CurrentW.Value.ToString("0.00") : "N/A"));
                    w.WriteLine("Max W: " + (p.MaxW.HasValue ? p.MaxW.Value.ToString("0.00") : "N/A"));
                }
            }
            return OperationResult.Ok("Compatibility report saved: " + path);
        }

        private bool ValidateA630(NvApiSession nv, PowerState publicState, out string reason)
        {
            reason = "";
            NvApiSession.GpuBufferDelegate fn = nv.GetGpuBuffer(A630Qi);
            if (fn == null) { reason = "QI 0x54924BF5 is unavailable"; return false; }

            byte[] b = new byte[A630Size];
            PutU32(b, 0, A630Version);
            IntPtr p = Marshal.AllocHGlobal(b.Length);
            try
            {
                Marshal.Copy(b, 0, p, b.Length);
                int rc = fn(nv.Gpu, p);
                Marshal.Copy(p, b, 0, b.Length);
                if (rc != 0) { reason = "A630 rc=" + rc.ToString(); return false; }
            }
            finally { Marshal.FreeHGlobal(p); }

            uint mask = U32(b, 0x04);
            if ((mask & 1u) == 0) { reason = "Board Power bit0 is absent"; return false; }
            uint min = U32(b, 0x30);
            uint def = U32(b, 0x34);
            uint max = U32(b, 0x38);
            if (min < 1000 || min > 100000 || def < min || max < def || max > 350000)
            { reason = "Board Power record is not plausible"; return false; }

            if (publicState != null && publicState.MaxW.HasValue)
            {
                uint expected = (uint)Math.Round(publicState.MaxW.Value * 1000.0);
                if (Math.Abs((long)max - (long)expected) > 1000)
                { reason = "A630 MAX does not match public MAX"; return false; }
            }

            reason = "A630 bit0 Board Power record validated.";
            return true;
        }

        private OperationResult SendE633(NvApiSession nv, long transportRva, int milliwatts, out int transportRc, out uint completion)
        {
            transportRc = -1;
            completion = 0;
            try
            {
                byte[] packet = new byte[PacketSize];
                PutU32(packet, 0x30, unchecked((uint)nv.Gpu.ToInt64()));
                PutU32(packet, 0x34, CmdE633);
                PutU32(packet, 0x38 + 0x04, 1);
                PutU32(packet, 0x38 + 0x2C, unchecked((uint)milliwatts));
                packet[0x38 + 0x30] = 0xF7;

                IntPtr fnPtr = new IntPtr(nv.ImplBase.ToInt64() + transportRva);
                InternalTransportDelegate transport = (InternalTransportDelegate)Marshal.GetDelegateForFunctionPointer(fnPtr, typeof(InternalTransportDelegate));
                IntPtr mem = Marshal.AllocHGlobal(PacketSize);
                try
                {
                    Marshal.Copy(packet, 0, mem, PacketSize);
                    transportRc = CurrentWriteGuard(() => transport(ApiRmControl, mem, PacketSize, 0, 1, 0, 0));
                    Marshal.Copy(mem, packet, 0, PacketSize);
                    completion = U32(packet, 0x2C);
                }
                finally { Marshal.FreeHGlobal(mem); }

                string diag = "transport=" + transportRc.ToString() + "; completion=0x" + completion.ToString("X8") + "; value=" + milliwatts.ToString();
                AppLog.Write("E633 " + diag);

                if (transportRc != 0 || completion != 1)
                {
                    if (completion == 0xFFFFEFE5)
                        return OperationResult.Fail("NVIDIA RM rejected E633 (0xFFFFEFE5). Close GPU workloads, wait a few seconds, and retry while GPU is idle.");
                    return OperationResult.Fail("E633 was not confirmed successful: " + diag);
                }
                return OperationResult.Ok(diag);
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("E633 transport exception: " + SafeError.Describe(ex));
            }
        }

        private static byte[] BuildOverride(int watts, int shadowOffset)
        {
            byte[] offset = BitConverter.GetBytes(shadowOffset);
            byte[] payload = BitConverter.GetBytes(watts * 1000);
            byte[] b = new byte[14];
            // bytes 0..4 wildcard match header
            for (int i = 0; i < 5; i++) b[i] = 0;
            Buffer.BlockCopy(offset, 0, b, 5, 4);
            b[9] = 4;
            Buffer.BlockCopy(payload, 0, b, 10, 4);
            return b;
        }

        private static bool IsToolOverride(byte[] b, GpuProfile profile, int shadowOffset)
        {
            if (profile == null) return false;
            foreach (int w in profile.Targets())
            {
                if (w != profile.StockPowerW && ByteArrayEqual(b, BuildOverride(w, shadowOffset))) return true;
                if (profile.AlternativeStockWatts != null)
                {
                    bool isAlt = false;
                    foreach (int alt in profile.AlternativeStockWatts) { if (w == alt) { isAlt = true; break; } }
                    if (!isAlt && ByteArrayEqual(b, BuildOverride(w, shadowOffset))) return true;
                }
            }
            return false;
        }

        private static bool ByteArrayEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private RegistryKey OpenNvidiaDriverKey(bool writable, string deviceInstanceId, out string nativePath)
        {
            nativePath = null;
            if (String.IsNullOrWhiteSpace(deviceInstanceId) ||
                !deviceInstanceId.StartsWith(@"PCI\VEN_10DE&", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The active NVIDIA PCI device identity is unavailable. Refresh compatibility.");

            // Windows records the active class-driver key on the exact PnP device.
            // DriverDesc is not unique: stale installations may have the same name.
            string driver;
            using (RegistryKey device = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum\" + deviceInstanceId, false))
            {
                if (device == null) throw new InvalidOperationException("Active NVIDIA PnP registry entry is missing.");
                driver = Convert.ToString(device.GetValue("Driver", ""));
                string classGuid = Convert.ToString(device.GetValue("ClassGUID", ""));
                if (!classGuid.Equals(DisplayClassGuid, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The selected PCI device is not a display adapter.");
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(driver,
                "^" + System.Text.RegularExpressions.Regex.Escape(DisplayClassGuid) + @"\\[0-9]{4}$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new InvalidOperationException("Windows returned an invalid NVIDIA driver-key mapping: " + driver);

            string path = @"SYSTEM\CurrentControlSet\Control\Class\" + driver;
            nativePath = @"HKLM\" + path;
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path, false))
            {
                if (key == null) throw new InvalidOperationException("Windows' active NVIDIA driver key is missing: " + nativePath);
                string hardwareMatch = Convert.ToString(key.GetValue("MatchingDeviceId", ""));
                if (String.IsNullOrWhiteSpace(hardwareMatch) ||
                    !deviceInstanceId.StartsWith(hardwareMatch, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Active driver key does not match the selected GPU PCI identity.");
            }
            try { return Registry.LocalMachine.OpenSubKey(path, writable); }
            catch (UnauthorizedAccessException ex)
            { throw new UnauthorizedAccessException("Administrator access is required for " + nativePath, ex); }
            catch (System.Security.SecurityException ex)
            { throw new UnauthorizedAccessException("Administrator access is required for " + nativePath, ex); }
        }
        private static string PsQuote(string value)
        {
            return (value ?? "").Replace("'", "''");
        }

        private static bool BackupRegistry(string nativePath, string reason, out string error)
        {
            error = "";
            try
            {
                string dir = Path.Combine(StatePaths.Dir);
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "backup-" + reason + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".reg");
                int rc;
                string output = SystemProbe.RunProcess("reg.exe", "export \"" + nativePath + "\" \"" + file + "\" /y", 30000, out rc);
                if (rc != 0 || !File.Exists(file)) { error = output.Trim(); return false; }
                return true;
            }
            catch (Exception ex) { error = SafeError.Describe(ex); return false; }
        }

        private static uint U32(byte[] b, int off)
        {
            return BitConverter.ToUInt32(b, off);
        }

        private static void PutU32(byte[] b, int off, uint value)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, off, 4);
        }
    }
}
