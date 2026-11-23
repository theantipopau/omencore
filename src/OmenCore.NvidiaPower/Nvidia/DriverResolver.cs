using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace NvpwrControlBlackwell
{
    internal static class DriverResolver
    {
        private const long Known61714TransportRva = 0x00390790;
        private const string Known61714ImplSha = "ff1527cea533c75f89033f7c0d9cdbaa6ec84bcac26c1db082227d68efd6a16e";
        private const string Known61714KmdSha = "101ae659a3cbaec04a749559cf227c87e9b5d310d5c8308c0dc1cf3fb9b037f6";

        private static readonly string CachePath = Path.Combine(
            StatePaths.Dir,
            "driver-resolver-cache.txt");

        // 617.14 internal NVIDIA RM transport prologue. Relative addresses are wildcarded.
        // This is intentionally long enough to be unique in the reviewed image.
        private static readonly string TransportPattern =
            "40 53 55 56 57 41 ?? 41 56 41 57 48 81 EC ?? 00 00 00 " +
            "48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? 00 00 00 " +
            "4D 8B F1 41 8B E8 48 8B F2 8B F9 8B 05 ?? ?? ?? ?? C1 E8 0D";

        private sealed class Section
        {
            public string Name = "";
            public int VirtualAddress;
            public int VirtualSize;
            public int RawPointer;
            public int RawSize;
            public uint Characteristics;
        }

        public static DriverResolution Resolve(
            string implPath,
            string implSha,
            string kmdSha,
            string deviceKey)
        {
            DriverResolution r = new DriverResolution();
            r.ImplSha256 = implSha ?? "";
            r.KmdSha256 = kmdSha ?? "";
            r.DeviceKey = deviceKey ?? "";

            try
            {
                bool known61714 =
                    String.Equals(implSha, Known61714ImplSha, StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(kmdSha, Known61714KmdSha, StringComparison.OrdinalIgnoreCase);

                if (known61714)
                {
                    r.CandidateFound = true;
                    r.Trusted = true;
                    r.KnownExact = true;
                    r.TransportRva = Known61714TransportRva;
                    r.Source = "built-in exact 617.14 driver profile";
                    r.Reason = "Exact reviewed 617.14 driver profile for Ada and Blackwell.";
                    return r;
                }

                DriverResolution cached;
                if (TryLoadCache(implSha, kmdSha, deviceKey, out cached))
                    return cached;

                if (String.IsNullOrEmpty(implPath) || !File.Exists(implPath))
                {
                    r.Reason = "nvapi64_impl.dll file is unavailable.";
                    return r;
                }

                byte[] image = File.ReadAllBytes(implPath);
                List<Section> sections;
                string peError;
                if (!ReadSections(image, out sections, out peError))
                {
                    r.Reason = "PE parse failed: " + peError;
                    return r;
                }

                byte?[] pattern = ParsePattern(TransportPattern);
                List<int> rawHits = new List<int>();

                foreach (Section s in sections)
                {
                    if ((s.Characteristics & 0x20000000u) == 0) continue; // IMAGE_SCN_MEM_EXECUTE
                    int start = Math.Max(0, s.RawPointer);
                    int end = Math.Min(image.Length, s.RawPointer + s.RawSize);
                    FindPattern(image, start, end, pattern, rawHits);
                }

                if (rawHits.Count != 1)
                {
                    r.Reason = "Driver transport signature was not unique (matches=" + rawHits.Count.ToString() + "). Writes remain locked.";
                    return r;
                }

                int rva;
                if (!RawToRva(rawHits[0], sections, out rva))
                {
                    r.Reason = "Transport signature matched but RVA conversion failed.";
                    return r;
                }

                r.CandidateFound = true;
                r.Trusted = false;
                r.KnownExact = false;
                r.TransportRva = rva;
                r.Source = "pattern resolver";
                r.Reason = "Unique transport candidate found at RVA 0x" + rva.ToString("X") + ". Run semantic no-op validation before writes.";
                return r;
            }
            catch (Exception ex)
            {
                r.Reason = "Driver resolver error: " + SafeError.Describe(ex);
                return r;
            }
        }

        public static void MarkTrusted(DriverResolution r)
        {
            if (r == null || !r.CandidateFound || r.TransportRva <= 0) return;
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
            string line = (r.ImplSha256 ?? "") + "|" +
                          (r.KmdSha256 ?? "") + "|" +
                          (r.DeviceKey ?? "") + "|" +
                          r.TransportRva.ToString("X", CultureInfo.InvariantCulture) + "|" +
                          DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            List<string> keep = new List<string>();
            if (File.Exists(CachePath))
            {
                foreach (string old in File.ReadAllLines(CachePath))
                {
                    string[] p = old.Split('|');
                    if (p.Length >= 3 &&
                        p[0].Equals(r.ImplSha256, StringComparison.OrdinalIgnoreCase) &&
                        p[1].Equals(r.KmdSha256, StringComparison.OrdinalIgnoreCase) &&
                        p[2].Equals(r.DeviceKey ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!String.IsNullOrWhiteSpace(old)) keep.Add(old);
                }
            }
            keep.Add(line);
            File.WriteAllLines(CachePath, keep.ToArray());
        }

        private static bool TryLoadCache(string implSha, string kmdSha, string deviceKey, out DriverResolution r)
        {
            r = null;
            try
            {
                if (!File.Exists(CachePath)) return false;
                foreach (string line in File.ReadAllLines(CachePath))
                {
                    string[] p = line.Split('|');
                    if (p.Length < 4) continue;
                    if (!p[0].Equals(implSha ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!p[1].Equals(kmdSha ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!p[2].Equals(deviceKey ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                    long rva;
                    if (!Int64.TryParse(p[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rva)) continue;
                    r = new DriverResolution();
                    r.CandidateFound = true;
                    r.Trusted = true;
                    r.KnownExact = false;
                    r.TransportRva = rva;
                    r.ImplSha256 = implSha ?? "";
                    r.KmdSha256 = kmdSha ?? "";
                    r.DeviceKey = deviceKey ?? "";
                    r.Source = "validated local cache";
                    r.Reason = "This exact UMD/KMD hash pair previously passed E633 no-op validation.";
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static byte?[] ParsePattern(string text)
        {
            string[] parts = text.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            byte?[] p = new byte?[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "??" || parts[i] == "?") p[i] = null;
                else p[i] = Byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return p;
        }

        private static void FindPattern(byte[] data, int start, int end, byte?[] pattern, List<int> hits)
        {
            int last = end - pattern.Length;
            for (int i = start; i <= last; i++)
            {
                bool ok = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (pattern[j].HasValue && data[i + j] != pattern[j].Value)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) hits.Add(i);
            }
        }

        private static bool ReadSections(byte[] b, out List<Section> sections, out string error)
        {
            sections = new List<Section>();
            error = "";
            try
            {
                if (b.Length < 0x100 || b[0] != 0x4D || b[1] != 0x5A) { error = "MZ header missing"; return false; }
                int pe = BitConverter.ToInt32(b, 0x3C);
                if (pe < 0 || pe + 0x108 > b.Length) { error = "invalid e_lfanew"; return false; }
                if (BitConverter.ToUInt32(b, pe) != 0x00004550) { error = "PE signature missing"; return false; }
                ushort count = BitConverter.ToUInt16(b, pe + 6);
                ushort opt = BitConverter.ToUInt16(b, pe + 20);
                int sh = pe + 24 + opt;
                if (sh + count * 40 > b.Length) { error = "section table outside file"; return false; }

                for (int i = 0; i < count; i++)
                {
                    int o = sh + i * 40;
                    int len = 0;
                    while (len < 8 && b[o + len] != 0) len++;
                    Section s = new Section();
                    s.Name = System.Text.Encoding.ASCII.GetString(b, o, len);
                    s.VirtualSize = BitConverter.ToInt32(b, o + 8);
                    s.VirtualAddress = BitConverter.ToInt32(b, o + 12);
                    s.RawSize = BitConverter.ToInt32(b, o + 16);
                    s.RawPointer = BitConverter.ToInt32(b, o + 20);
                    s.Characteristics = BitConverter.ToUInt32(b, o + 36);
                    sections.Add(s);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = SafeError.Describe(ex);
                return false;
            }
        }

        private static bool RawToRva(int raw, List<Section> sections, out int rva)
        {
            rva = 0;
            foreach (Section s in sections)
            {
                if (raw >= s.RawPointer && raw < s.RawPointer + s.RawSize)
                {
                    rva = s.VirtualAddress + (raw - s.RawPointer);
                    return true;
                }
            }
            return false;
        }
    }
}
