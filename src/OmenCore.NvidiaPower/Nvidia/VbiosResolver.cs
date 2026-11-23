using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace NvpwrControlBlackwell
{
    internal static class VbiosResolver
    {
        private static readonly string CachePath = Path.Combine(
            StatePaths.Dir,
            "vbios-resolver-cache-v3.txt");

        private sealed class RomImage
        {
            public int Start;
            public int End;
            public int DeviceId;
            public int CodeType;
        }

        private sealed class PowerCandidate
        {
            public RomImage Image;
            public int TableOffset;
            public int EntryOffset;
            public int EntryIndex;
            public int MaxFieldOffset;
            public uint Min;
            public uint Default;
            public uint Max;
            public uint BasePower;
        }

        public static VbiosResolution ResolveKnownOrCache(GpuProfile profile, string vbios, string deviceKey)
        {
            VbiosResolution r = new VbiosResolution();
            if (profile == null)
            {
                r.Reason = "GPU profile is unknown.";
                return r;
            }

            // Live-verified reference machine.
            if (profile.Id == "5070ti" &&
                String.Equals(vbios, "98.05.4E.00.07", StringComparison.OrdinalIgnoreCase) &&
                !String.IsNullOrEmpty(deviceKey) &&
                deviceKey.IndexOf("DEV_2F58", StringComparison.OrdinalIgnoreCase) >= 0 &&
                deviceKey.IndexOf("SUBSYS_803F17AA", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                r.Resolved = true;
                r.KnownExact = true;
                r.Source = "built-in verified VBIOS profile";
                r.Reason = "Reference RTX 5070 Ti VBIOS profile verified.";
                r.StockMaxW = 140;
                r.ShadowOffset = 0x000580F4;
                r.PowerTableRawOffset = 0x0008DBDB;
                r.MaxFieldRawOffset = 0x0008DCF4;
                r.LegacyImageRawOffset = 0x00035C00;
                r.RomImageCount = 1;
                r.MatchingRomImageCount = 1;
                r.PcirDeviceId = 0x2F58;
                r.CandidateCount = 1;
                r.EntryIndex = (r.MaxFieldRawOffset - (r.PowerTableRawOffset + 0x38) - 0x15) / 0x66;
                r.Layout = "Power Budget v0x40 / header 0x38 / entry 0x66";
                r.RecordMinMw = 5000;
                r.RecordDefaultMw = 70000;
                r.RecordMaxMw = 140000;
                r.RecordBaseMw = 115000;
                r.SemanticScore = 100;
                r.Confidence = "reference-exact";
                return r;
            }

            VbiosResolution cached;
            if (TryLoadCache(profile.Id, vbios, deviceKey, out cached)) return cached;

            r.Source = "unresolved";
            r.Reason = "Unknown VBIOS. The automatic read-only ROM resolver must validate this exact GPU/VBIOS before MAX writes are enabled.";
            r.StockMaxW = profile.StockPowerW;
            return r;
        }

        public static VbiosResolution ResolveFromRom(string romPath, GpuProfile profile, string vbiosVersion, string deviceKey)
        {
            VbiosResolution r = new VbiosResolution();
            r.Source = "ROM structural resolver v3";
            r.RomPath = romPath ?? "";
            r.StockMaxW = profile != null ? profile.StockPowerW : 0;
            r.Layout = "Power Budget v0x40 / header 0x38 / entry 0x66";

            try
            {
                if (profile == null)
                {
                    r.Reason = "GPU profile is unknown.";
                    return r;
                }
                if (String.IsNullOrEmpty(romPath) || !File.Exists(romPath))
                {
                    r.Reason = "ROM file does not exist.";
                    return r;
                }

                byte[] b = File.ReadAllBytes(romPath);
                if (b.Length < 0x10000)
                {
                    r.Reason = "ROM image is unexpectedly small.";
                    return r;
                }

                r.RomSha256 = SystemProbe.Sha256File(romPath);

                List<RomImage> allImages = FindNvidiaImages(b);
                List<RomImage> legacyImages = new List<RomImage>();
                foreach (RomImage image in allImages)
                    if (image.CodeType == 0) legacyImages.Add(image);

                r.RomImageCount = legacyImages.Count;
                if (legacyImages.Count == 0)
                {
                    r.Reason = "No NVIDIA legacy code-type-0 image was found in the ROM dump.";
                    return r;
                }

                // Bound legacy images by the start of the next legacy image or the end of the ROM
                for (int i = 0; i < legacyImages.Count; i++)
                {
                    int nextStart = (i + 1 < legacyImages.Count) ? legacyImages[i + 1].Start : b.Length;
                    if (nextStart <= legacyImages[i].Start || nextStart > b.Length) nextStart = b.Length;
                    legacyImages[i].End = nextStart;
                }

                int expectedDeviceId = ParsePnpDeviceId(deviceKey);
                List<RomImage> matchingImages = new List<RomImage>();
                if (expectedDeviceId > 0)
                {
                    foreach (RomImage image in legacyImages)
                        if (image.DeviceId == expectedDeviceId) matchingImages.Add(image);

                    if (matchingImages.Count == 0)
                    {
                        r.Reason = "ROM identity mismatch: GPU PnP DEV_" + expectedDeviceId.ToString("X4") +
                                   " does not match any NVIDIA legacy ROM image.";
                        return r;
                    }
                }
                else
                {
                    // If Windows did not expose a parsable DEV_xxxx token, keep
                    // all NVIDIA code-0 images and require a unique power record.
                    matchingImages.AddRange(legacyImages);
                }

                r.MatchingRomImageCount = matchingImages.Count;

                List<PowerCandidate> candidates = new List<PowerCandidate>();
                foreach (RomImage image in matchingImages)
                    FindPowerCandidates(b, image, profile.StockPowerW, candidates);

                // If default stock was not found, try alternative stock wattages if profile defines them (e.g. 150 W for 4080/4090)
                if (candidates.Count == 0 && profile.AlternativeStockWatts != null)
                {
                    foreach (int altStock in profile.AlternativeStockWatts)
                    {
                        foreach (RomImage image in matchingImages)
                            FindPowerCandidates(b, image, altStock, candidates);
                        if (candidates.Count > 0) break;
                    }
                }

                // Deduplicate candidates that share the same normalized shadow offset across mirror legacy images
                HashSet<int> uniqueShadows = new HashSet<int>();
                List<PowerCandidate> dedupedCandidates = new List<PowerCandidate>();
                foreach (PowerCandidate cand in candidates)
                {
                    int shadowOffset = cand.MaxFieldOffset - cand.Image.Start;
                    if (uniqueShadows.Add(shadowOffset))
                    {
                        dedupedCandidates.Add(cand);
                    }
                }
                candidates = dedupedCandidates;

                r.CandidateCount = candidates.Count;
                if (candidates.Count != 1)
                {
                    r.Reason = "Power Budget Board-Power MAX record was not unique for stock " +
                               profile.StockPowerW.ToString(CultureInfo.InvariantCulture) + " W (matches=" +
                               candidates.Count.ToString(CultureInfo.InvariantCulture) +
                               ", matching legacy images=" + matchingImages.Count.ToString(CultureInfo.InvariantCulture) + ").";
                    return r;
                }

                PowerCandidate c = candidates[0];
                int detectedStockW = (int)(c.Max / 1000);
                int rawMax = c.MaxFieldOffset;
                int shadow = rawMax - c.Image.Start;
                if (shadow <= 0 || shadow >= b.Length - c.Image.Start)
                {
                    r.Reason = "Resolved normalized shadow offset is outside the selected NVIDIA ROM image.";
                    return r;
                }

                // The exact reference remains an invariant test for the generic resolver.
                if (profile.Id == "5070ti" &&
                    String.Equals(vbiosVersion, "98.05.4E.00.07", StringComparison.OrdinalIgnoreCase) &&
                    shadow != 0x580F4)
                {
                    r.Reason = "Reference VBIOS geometry mismatch: expected shadow offset 0x580F4, got 0x" + shadow.ToString("X") + ".";
                    return r;
                }

                r.Resolved = true;
                r.KnownExact = false;
                r.Reason = "Unique device-matched Power Budget Board-Power MAX field resolved from ROM.";
                r.StockMaxW = detectedStockW;
                r.ShadowOffset = shadow;
                r.MaxFieldRawOffset = rawMax;
                r.PowerTableRawOffset = c.TableOffset;
                r.LegacyImageRawOffset = c.Image.Start;
                r.PcirDeviceId = c.Image.DeviceId;
                r.EntryIndex = c.EntryIndex;
                r.RecordMinMw = (int)c.Min;
                r.RecordDefaultMw = (int)c.Default;
                r.RecordMaxMw = (int)c.Max;
                r.RecordBaseMw = (int)c.BasePower;
                r.SemanticScore = ScoreCandidate(c, detectedStockW);
                r.Confidence = r.SemanticScore >= 90 ? "high" : (r.SemanticScore >= 75 ? "medium" : "low");
                r.Layout = (b[c.TableOffset] == 0x30) ? "Power Budget v0x30 / Ada Lovelace" : "Power Budget v0x40 / header 0x38 / entry 0x66";

                if (r.SemanticScore < 75)
                {
                    r.Resolved = false;
                    r.Reason = "Unique Power Budget record found, but semantic confidence is too low (score=" +
                               r.SemanticScore.ToString(CultureInfo.InvariantCulture) + ").";
                    return r;
                }

                try { SaveCache(profile.Id, vbiosVersion, deviceKey, r); } catch { }
                return r;
            }
            catch (Exception ex)
            {
                r.Reason = "VBIOS resolver error: " + SafeError.Describe(ex);
                return r;
            }
        }

        public static bool TryAutoDumpRom(out string romPath, out string message)
        {
            romPath = "";
            message = "";
            try
            {
                string exe = FindNvflash();
                if (String.IsNullOrEmpty(exe))
                {
                    message = "nvflash64.exe (or nvflashb.exe on RTX 50 cards) was not found. Select a .rom file manually. Apply CURRENT does not need it.";
                    return false;
                }

                string dir = Path.Combine(StatePaths.Dir, "rom-cache");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "auto-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".rom");

                int rc;
                // Read-only dump attempts. No flash/program command is used.
                string output = SystemProbe.RunProcess(exe, "--save=\"" + path + "\"", 30000, true, out rc);

                if (rc != 0 || !File.Exists(path) || new FileInfo(path).Length < 0x10000)
                    output += "\r\n" + SystemProbe.RunProcess(exe, "--save \"" + path + "\"", 30000, true, out rc);

                if (rc != 0 || !File.Exists(path) || new FileInfo(path).Length < 0x10000)
                    output += "\r\n" + SystemProbe.RunProcess(exe, "-b \"" + path + "\"", 30000, true, out rc);

                if (rc == 0 && File.Exists(path) && new FileInfo(path).Length >= 0x10000)
                {
                    romPath = path;
                    message = "VBIOS dumped read-only through nvflash: " + path;
                    return true;
                }

                message = "nvflash was found but ROM dump failed: " + output.Trim();
                return false;
            }
            catch (Exception ex)
            {
                message = "Automatic ROM dump failed: " + SafeError.Describe(ex);
                return false;
            }
        }

        private static List<RomImage> FindNvidiaImages(byte[] b)
        {
            List<RomImage> hits = new List<RomImage>();
            for (int s = 0; s + 0x1A < b.Length; s++)
            {
                if (b[s] != 0x55 || b[s + 1] != 0xAA) continue;

                int pcirRel = BitConverter.ToUInt16(b, s + 0x18);
                if (pcirRel <= 0) continue;
                int p = s + pcirRel;
                if (p < 0 || p + 0x18 > b.Length) continue;
                if (b[p] != (byte)'P' || b[p + 1] != (byte)'C' || b[p + 2] != (byte)'I' || b[p + 3] != (byte)'R') continue;

                int vendor = BitConverter.ToUInt16(b, p + 4);
                if (vendor != 0x10DE) continue;

                int device = BitConverter.ToUInt16(b, p + 6);
                int codeType = b[p + 0x14];

                RomImage image = new RomImage();
                image.Start = s;
                image.DeviceId = device;
                image.CodeType = codeType;
                hits.Add(image);
            }

            hits.Sort(delegate(RomImage a, RomImage c) { return a.Start.CompareTo(c.Start); });

            for (int i = 0; i < hits.Count; i++)
            {
                int nextStart = i + 1 < hits.Count ? hits[i + 1].Start : b.Length;

                // Use the next real NVIDIA PCI image as the structural boundary.
                // This is more robust than trusting a vendor-specific declared
                // image length when data tables are stored before the next image.
                int end = nextStart;
                if (end <= hits[i].Start || end > b.Length) end = b.Length;
                hits[i].End = end;
            }

            return hits;
        }

        private static void FindPowerCandidates(byte[] b, RomImage image, int stockPowerW, List<PowerCandidate> output)
        {
            int start = Math.Max(0, image.Start);
            int end = Math.Min(b.Length, image.End > image.Start ? image.End : b.Length);
            uint expectedMax = (uint)(stockPowerW * 1000);

            for (int t = start; t + 0x30 <= end; t++)
            {
                // Support Ada Lovelace (0x30) and Blackwell (0x40) Power Budget table layouts.
                byte ver = b[t];
                if (ver != 0x40 && ver != 0x30) continue;
                int hdr = b[t + 1];
                int esize = b[t + 2];
                if (hdr < 0x20 || hdr > 0x60 || esize < 0x40 || esize > 0x80) continue;

                int count = b[t + 3];
                if (count < 1 || count > 64) continue;
                long tableEnd = (long)t + (long)hdr + (long)count * (long)esize;
                if (tableEnd > end) continue;

                for (int i = 0; i < count; i++)
                {
                    int e = t + hdr + i * esize;
                    uint min, def, max, basePower;
                    int maxFieldOffset;

                    if (ver == 0x40) // Blackwell
                    {
                        if (e + 0x19 > end) break;
                        min = U32(b, e + 0x0D);
                        def = U32(b, e + 0x11);
                        max = U32(b, e + 0x15);
                        basePower = (esize >= 0x4D && e + 0x4D <= end) ? U32(b, e + 0x49) : def;
                        maxFieldOffset = e + 0x15;
                    }
                    else // 0x30 Ada Lovelace
                    {
                        if (e + 0x22 > end) break;
                        min = U32(b, e + 0x02);
                        def = U32(b, e + 0x06);
                        max = U32(b, e + 0x0A);
                        basePower = U32(b, e + 0x1A);
                        maxFieldOffset = e + 0x0A;
                    }

                    if (max != expectedMax) continue;
                    if (!PlausiblePowerRecord(min, def, max, basePower)) continue;

                    PowerCandidate c = new PowerCandidate();
                    c.Image = image;
                    c.TableOffset = t;
                    c.EntryOffset = e;
                    c.EntryIndex = i;
                    c.MaxFieldOffset = maxFieldOffset;
                    c.Min = min;
                    c.Default = def;
                    c.Max = max;
                    c.BasePower = basePower;
                    output.Add(c);
                }
            }
        }

        private static int ScoreCandidate(PowerCandidate c, int stockPowerW)
        {
            if (c == null) return 0;
            int score = 0;
            uint expected = (uint)(stockPowerW * 1000);
            if (c.Max == expected) score += 45;
            if (c.Default >= c.Min && c.Default <= c.Max) score += 15;
            if (c.BasePower > 0 && c.BasePower <= c.Max) score += 15;
            if ((c.Max - c.BasePower) <= 130000) score += 10;
            if (c.Min <= 25000) score += 5;
            if (c.Image != null && c.Image.CodeType == 0) score += 5;
            if (c.Image != null && c.Image.DeviceId != 0) score += 5;
            return Math.Min(100, score);
        }

        private static bool PlausiblePowerRecord(uint min, uint def, uint max, uint basePower)
        {
            if (min < 1000 || min > 200000) return false;
            if (def < 10000 || def > 400000) return false;
            if (max < 50000 || max > 500000) return false;
            if (basePower < 10000 || basePower > 400000) return false;
            if (min > max || def > max || basePower > max) return false;
            return true;
        }

        private static int ParsePnpDeviceId(string deviceKey)
        {
            if (String.IsNullOrEmpty(deviceKey)) return 0;
            int p = deviceKey.IndexOf("DEV_", StringComparison.OrdinalIgnoreCase);
            if (p < 0 || p + 8 > deviceKey.Length) return 0;
            int value;
            if (Int32.TryParse(deviceKey.Substring(p + 4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                return value;
            return 0;
        }

        private static uint U32(byte[] b, int off)
        {
            return BitConverter.ToUInt32(b, off);
        }

        // nvflash64.exe is the classic name; Blackwell (RTX 50) cards use nvflashb.exe (Prophecy standalone 1.2.0 notes).
        // Neither is shipped here: we only find a copy the user supplied.
        internal static readonly string[] NvflashFileNames = { "nvflash64.exe", "nvflashb.exe" };

        internal static string FindNvflash(string baseDirectory, string pathVariable, Func<string, bool> fileExists)
        {
            List<string> candidates = new List<string>();
            foreach (string name in NvflashFileNames)
            {
                candidates.Add(Path.Combine(baseDirectory, "tools", name));
                candidates.Add(Path.Combine(baseDirectory, name));
                candidates.Add(@"C:\NVFlash\" + name);
                candidates.Add(@"C:\nvflash\" + name);
                foreach (string d in pathVariable.Split(';'))
                {
                    if (!String.IsNullOrWhiteSpace(d)) candidates.Add(Path.Combine(d.Trim(), name));
                }
            }

            foreach (string c in candidates)
                try { if (fileExists(c)) return Path.GetFullPath(c); } catch (ArgumentException) { } catch (NotSupportedException) { }
            return null;
        }

        private static string FindNvflash() =>
            FindNvflash(AppDomain.CurrentDomain.BaseDirectory, Environment.GetEnvironmentVariable("PATH") ?? "", File.Exists);

        private static void SaveCache(string profileId, string vbiosVersion, string deviceKey, VbiosResolution r)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
            string key = (profileId ?? "") + "|" + (vbiosVersion ?? "") + "|" + (deviceKey ?? "");
            string line = key + "|" + (r.RomSha256 ?? "") + "|" +
                          r.StockMaxW.ToString(CultureInfo.InvariantCulture) + "|" +
                          r.ShadowOffset.ToString("X", CultureInfo.InvariantCulture) + "|" +
                          r.PowerTableRawOffset.ToString("X", CultureInfo.InvariantCulture) + "|" +
                          r.MaxFieldRawOffset.ToString("X", CultureInfo.InvariantCulture) + "|" +
                          r.LegacyImageRawOffset.ToString("X", CultureInfo.InvariantCulture) + "|" +
                          r.PcirDeviceId.ToString("X", CultureInfo.InvariantCulture) + "|" +
                          r.RomImageCount.ToString(CultureInfo.InvariantCulture) + "|" +
                          r.MatchingRomImageCount.ToString(CultureInfo.InvariantCulture) + "|" +
                          r.CandidateCount.ToString(CultureInfo.InvariantCulture) + "|" +
                          r.EntryIndex.ToString(CultureInfo.InvariantCulture) + "|" +
                          r.RecordMinMw.ToString(CultureInfo.InvariantCulture) + "|" +
                          r.RecordDefaultMw.ToString(CultureInfo.InvariantCulture) + "|" +
                          r.RecordMaxMw.ToString(CultureInfo.InvariantCulture) + "|" +
                          r.RecordBaseMw.ToString(CultureInfo.InvariantCulture) + "|" +
                          r.SemanticScore.ToString(CultureInfo.InvariantCulture) + "|" +
                          (r.Confidence ?? "");

            List<string> keep = new List<string>();
            if (File.Exists(CachePath))
            {
                foreach (string old in File.ReadAllLines(CachePath))
                {
                    if (!old.StartsWith(key + "|", StringComparison.OrdinalIgnoreCase) && !String.IsNullOrWhiteSpace(old)) keep.Add(old);
                }
            }
            keep.Add(line);
            File.WriteAllLines(CachePath, keep.ToArray());
        }

        private static bool TryLoadCache(string profileId, string vbiosVersion, string deviceKey, out VbiosResolution r)
        {
            r = null;
            try
            {
                if (!File.Exists(CachePath)) return false;
                string key = (profileId ?? "") + "|" + (vbiosVersion ?? "") + "|" + (deviceKey ?? "");
                foreach (string line in File.ReadAllLines(CachePath))
                {
                    if (!line.StartsWith(key + "|", StringComparison.OrdinalIgnoreCase)) continue;
                    string[] p = line.Split('|');
                    if (p.Length < 19) continue;

                    int stock, shadow, table, rawMax, legacy, pcirDevice, imageCount, matchingCount, candidateCount, entryIndex;
                    int recordMin, recordDefault, recordMax, recordBase, semanticScore;
                    if (!Int32.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out stock)) continue;
                    if (!Int32.TryParse(p[5], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out shadow)) continue;
                    if (!Int32.TryParse(p[6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out table)) continue;
                    if (!Int32.TryParse(p[7], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rawMax)) continue;
                    if (!Int32.TryParse(p[8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out legacy)) continue;
                    if (!Int32.TryParse(p[9], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out pcirDevice)) continue;
                    if (!Int32.TryParse(p[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out imageCount)) continue;
                    if (!Int32.TryParse(p[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out matchingCount)) continue;
                    if (!Int32.TryParse(p[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out candidateCount)) continue;
                    if (!Int32.TryParse(p[13], NumberStyles.Integer, CultureInfo.InvariantCulture, out entryIndex)) continue;
                    if (!Int32.TryParse(p[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out recordMin)) continue;
                    if (!Int32.TryParse(p[15], NumberStyles.Integer, CultureInfo.InvariantCulture, out recordDefault)) continue;
                    if (!Int32.TryParse(p[16], NumberStyles.Integer, CultureInfo.InvariantCulture, out recordMax)) continue;
                    if (!Int32.TryParse(p[17], NumberStyles.Integer, CultureInfo.InvariantCulture, out recordBase)) continue;
                    if (!Int32.TryParse(p[18], NumberStyles.Integer, CultureInfo.InvariantCulture, out semanticScore)) continue;

                    r = new VbiosResolution();
                    r.Resolved = true;
                    r.KnownExact = false;
                    r.Source = "validated local VBIOS resolver-v3 cache";
                    r.Reason = "This exact GPU/VBIOS identity was previously resolved by the device-matched structural ROM resolver.";
                    r.RomSha256 = p[3];
                    r.StockMaxW = stock;
                    r.ShadowOffset = shadow;
                    r.PowerTableRawOffset = table;
                    r.MaxFieldRawOffset = rawMax;
                    r.LegacyImageRawOffset = legacy;
                    r.PcirDeviceId = pcirDevice;
                    r.RomImageCount = imageCount;
                    r.MatchingRomImageCount = matchingCount;
                    r.CandidateCount = candidateCount;
                    r.EntryIndex = entryIndex;
                    r.RecordMinMw = recordMin;
                    r.RecordDefaultMw = recordDefault;
                    r.RecordMaxMw = recordMax;
                    r.RecordBaseMw = recordBase;
                    r.SemanticScore = semanticScore;
                    r.Confidence = p.Length > 19 ? p[19] : (semanticScore >= 90 ? "high" : "medium");
                    r.Layout = (profileId != null && profileId.StartsWith("40"))
                        ? "Power Budget v0x30 / Ada Lovelace"
                        : "Power Budget v0x40 / header 0x38 / entry 0x66";
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
