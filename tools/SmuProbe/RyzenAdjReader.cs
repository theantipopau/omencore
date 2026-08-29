// The independent readback oracle: an external ryzenadj.exe, used READ ONLY.
//
// Shared by --limits and --pmtable. --pmtable needs it to anchor the PM table layout
// against values a second implementation already agrees on, which is the whole method:
// an offset guessed from another codename is how you get a confident wrong answer.

using System.Diagnostics;
using System.Globalization;

namespace OmenCore.Tools.SmuProbe;

internal sealed class SmuSnapshot
{
    public double StapmLimit, StapmValue;
    public double FastLimit, SlowLimit, ApuLimit;
    public double TdcLimit, TdcValue, EdcLimit, ThmLimit, ThmValue;
}

/// <summary>
/// Reads the SMU power table by shelling out to an external ryzenadj.exe. READ ONLY - this
/// type has no write path and never passes a --*-limit argument.
/// </summary>
internal sealed class RyzenAdjReader
{
    private readonly string _exe;
    private RyzenAdjReader(string exe) => _exe = exe;

    internal static RyzenAdjReader? TryCreate(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? new RyzenAdjReader(path) : null;

    internal SmuSnapshot? Read()
    {
        var rows = ReadRows();
        if (rows == null) return null;

        return new SmuSnapshot
        {
            StapmLimit = Get(rows, "STAPM LIMIT"),
            StapmValue = Get(rows, "STAPM VALUE"),
            FastLimit = Get(rows, "PPT LIMIT FAST"),
            SlowLimit = Get(rows, "PPT LIMIT SLOW"),
            ApuLimit = Get(rows, "PPT LIMIT APU"),
            TdcLimit = Get(rows, "TDC LIMIT VDD"),
            TdcValue = Get(rows, "TDC VALUE VDD"),
            EdcLimit = Get(rows, "EDC LIMIT VDD"),
            ThmLimit = Get(rows, "THM LIMIT CORE"),
            ThmValue = Get(rows, "THM VALUE CORE")
        };

        static double Get(Dictionary<string, double> r, string key) =>
            r.TryGetValue(key, out double v) ? v : 0;
    }

    /// <summary>Row label as ryzenadj prints it, to its value.</summary>
    private Dictionary<string, double>? ReadRows()
    {
        try
        {
            var psi = new ProcessStartInfo(_exe, "--info")
            {
                // ryzenadj loads its own DLLs by bare name, so it has to run from its directory.
                WorkingDirectory = Path.GetDirectoryName(_exe)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null) return null;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(20000);

            // | STAPM LIMIT | 45.000 | stapm-limit |   ->  cells 1 = label, 2 = value
            var rows = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in output.Split('\n'))
            {
                var cells = line.Split('|', StringSplitOptions.TrimEntries);
                if (cells.Length < 4 || cells[1].Length == 0) continue;
                if (double.TryParse(cells[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    rows[cells[1]] = v;
            }

            return rows.Count > 0 ? rows : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (ryzenadj read failed: {ex.Message})");
            return null;
        }
    }
}
