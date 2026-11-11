using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Utils;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    /// <summary>
    /// The bug these cover was that the timeout could not fire at all: every caller read the
    /// child's output to completion first, and ReadToEnd only returns when the child closes the
    /// pipe. These tests exist to keep the wait boundary honest, not to exercise Process itself.
    /// </summary>
    public class ProcessWaitTests
    {
        /// <summary>A child that outlives the timeout, with its output left open.</summary>
        private static Process StartHungChild()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"Start-Sleep -Seconds 60\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = Process.Start(psi);
            process.Should().NotBeNull();
            return process!;
        }

        [Fact]
        public void TryWait_ReturnsFalseAndKillsChild_WhenChildOutlivesTheTimeout()
        {
            using var process = StartHungChild();
            var outputTask = process.StandardOutput.ReadToEndAsync();

            var stopwatch = Stopwatch.StartNew();
            var exited = ProcessWait.TryWait(process, 500);
            stopwatch.Stop();

            exited.Should().BeFalse("the child sleeps for 60 s, far longer than the 500 ms bound");
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20),
                "the bound has to be enforced, not waited out - the old ReadToEnd() first pattern blocked here");

            // Kill is best effort and asynchronous; give the OS a moment to reap it.
            process.WaitForExit(5000).Should().BeTrue("TryWait kills the process tree on timeout");
            ProcessWait.Complete(outputTask).Should().NotBeNull();
        }

        [Fact]
        public void TryWait_ReturnsTrue_WhenChildExitsInsideTheBound()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c echo ok",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            process.Should().NotBeNull();

            var outputTask = process!.StandardOutput.ReadToEndAsync();

            ProcessWait.TryWait(process!, 15000).Should().BeTrue();
            ProcessWait.Complete(outputTask).Trim().Should().Be("ok");
        }
    }
}
