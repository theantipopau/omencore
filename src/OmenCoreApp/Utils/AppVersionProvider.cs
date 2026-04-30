using System;
using System.IO;
using System.Reflection;

namespace OmenCore.Utils
{
    internal static class AppVersionProvider
    {
        public static string GetVersionString()
        {
            var versionFromFile = TryGetVersionFromFile();
            var versionFromAssembly = TryGetVersionFromAssembly();

            if (!string.IsNullOrWhiteSpace(versionFromFile))
            {
                if (ShouldPreferAssemblyVersion(versionFromFile, versionFromAssembly))
                {
                    return versionFromAssembly!;
                }

                return versionFromFile;
            }

            if (!string.IsNullOrWhiteSpace(versionFromAssembly))
            {
                return versionFromAssembly;
            }

            return "Unknown";
        }

        private static string? TryGetVersionFromFile()
        {
            try
            {
                var versionFile = Path.Combine(AppContext.BaseDirectory, "VERSION.txt");
                if (File.Exists(versionFile))
                {
                    foreach (var line in File.ReadLines(versionFile))
                    {
                        var candidate = line.Trim();
                        if (!string.IsNullOrEmpty(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch
            {
                // Fall back to assembly metadata.
            }

            return null;
        }

        private static string? TryGetVersionFromAssembly()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var informationalVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(informationalVersion))
                {
                    return NormalizeDisplayVersion(informationalVersion);
                }

                var fileVersion = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
                if (!string.IsNullOrWhiteSpace(fileVersion))
                {
                    return NormalizeDisplayVersion(fileVersion);
                }

                var asmVersion = asm.GetName().Version?.ToString();
                if (!string.IsNullOrWhiteSpace(asmVersion))
                {
                    return NormalizeDisplayVersion(asmVersion);
                }
            }
            catch
            {
                // Final fallback below.
            }

            return null;
        }

        private static bool ShouldPreferAssemblyVersion(string versionFromFile, string? versionFromAssembly)
        {
            if (string.IsNullOrWhiteSpace(versionFromAssembly))
            {
                return false;
            }

            return TryParseVersionCore(versionFromAssembly, out var assemblyVersion)
                && TryParseVersionCore(versionFromFile, out var fileVersion)
                && assemblyVersion > fileVersion;
        }

        private static bool TryParseVersionCore(string value, out Version version)
        {
            var core = value.Split('-', '+')[0];
            return Version.TryParse(core, out version!);
        }

        private static string NormalizeDisplayVersion(string version)
        {
            return version.Split('+')[0];
        }
    }
}
