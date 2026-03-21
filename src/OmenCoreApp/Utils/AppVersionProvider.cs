using System;
using System.IO;
using System.Reflection;

namespace OmenCore.Utils
{
    internal static class AppVersionProvider
    {
        public static string GetVersionString()
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

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var fileVersion = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
                if (!string.IsNullOrWhiteSpace(fileVersion))
                {
                    return fileVersion;
                }

                var asmVersion = asm.GetName().Version?.ToString();
                if (!string.IsNullOrWhiteSpace(asmVersion))
                {
                    return asmVersion;
                }
            }
            catch
            {
                // Final fallback below.
            }

            return "Unknown";
        }
    }
}