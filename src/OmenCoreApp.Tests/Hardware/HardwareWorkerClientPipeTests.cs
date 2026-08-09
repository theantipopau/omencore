using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Regression coverage for the v3.8.2 hang fix: HardwareWorkerClient.SendRequestAsync
    /// previously reused the same NamedPipeClientStream across every request with no
    /// serialization and no recovery after a timed-out read. A slow worker response (or
    /// any concurrent callers) could leave a response message un-consumed in the pipe
    /// buffer, so the *next* request would read a stale/misrouted reply instead of its own
    /// — permanently desyncing the connection. That manifested in the field as repeated
    /// "temperature appears frozen" warnings followed by a full Application Hang
    /// (Event ID 1002, HangType=Cross-process) on OMEN 16-xd0xxx (ProductId 8BCD), which
    /// has worker-backed CPU temperature override enabled and so exercises this path on
    /// every monitoring cycle.
    /// </summary>
    public class HardwareWorkerClientPipeTests
    {
        private static (HardwareWorkerClient Client, FieldInfo PipeField, MethodInfo SendMethod) CreateClientWithReflectionAccess()
        {
            var client = new HardwareWorkerClient();
            var pipeField = typeof(HardwareWorkerClient).GetField("_pipeClient", BindingFlags.Instance | BindingFlags.NonPublic);
            var sendMethod = typeof(HardwareWorkerClient).GetMethod("SendRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic);

            pipeField.Should().NotBeNull();
            sendMethod.Should().NotBeNull();

            return (client, pipeField!, sendMethod!);
        }

        private static async Task<(NamedPipeServerStream Server, NamedPipeClientStream Client)> CreateConnectedTestPipeAsync()
        {
            var pipeName = "OmenCoreTest_" + Guid.NewGuid().ToString("N");
            var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
            var clientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            var connectTask = clientStream.ConnectAsync(2000);
            await server.WaitForConnectionAsync();
            await connectTask;

            return (server, clientStream);
        }

        [Fact]
        public async Task SendRequestAsync_DisposesPipe_WhenServerNeverResponds_SoNextCallReconnectsInsteadOfReadingStaleData()
        {
            var (server, clientStream) = await CreateConnectedTestPipeAsync();
            using var serverDisposable = server;

            var (client, pipeField, sendMethod) = CreateClientWithReflectionAccess();
            pipeField.SetValue(client, clientStream);

            // Server intentionally never reads/replies — simulates a worker that's too busy
            // (GC pause, driver call) to respond inside the client's request timeout.
            var resultTask = (Task<string>)sendMethod.Invoke(client, new object[] { "GET" })!;
            var result = await resultTask;

            result.Should().BeEmpty();
            pipeField.GetValue(client).Should().BeNull(
                "a timed-out read must tear the connection down rather than leave a pipe that may still receive a late, now-unmatched response");
        }

        [Fact]
        public async Task SendRequestAsync_SerializesConcurrentCalls_SoEachCallerGetsItsOwnMatchingResponse()
        {
            var (server, clientStream) = await CreateConnectedTestPipeAsync();
            using var serverDisposable = server;

            var (client, pipeField, sendMethod) = CreateClientWithReflectionAccess();
            pipeField.SetValue(client, clientStream);

            const int requestCount = 5;

            var serverTask = Task.Run(async () =>
            {
                var buffer = new byte[256];
                for (int i = 0; i < requestCount; i++)
                {
                    var bytesRead = await server.ReadAsync(buffer, 0, buffer.Length);
                    var req = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    // Simulate a worker that isn't instantaneous, to widen the window for
                    // any caller interleaving the previous (unserialized) code permitted.
                    await Task.Delay(30);

                    var replyBytes = Encoding.UTF8.GetBytes($"ECHO:{req}");
                    await server.WriteAsync(replyBytes, 0, replyBytes.Length);
                    await server.FlushAsync();
                }
            });

            var calls = Enumerable.Range(0, requestCount)
                .Select(i => (Task<string>)sendMethod.Invoke(client, new object[] { $"REQ{i}" })!)
                .ToArray();

            var results = await Task.WhenAll(calls);
            await serverTask;

            for (int i = 0; i < requestCount; i++)
            {
                results[i].Should().Be($"ECHO:REQ{i}", "each caller must receive the response to its own request, never another caller's");
            }
        }

        /// <summary>
        /// Dispose() used to wrap StopAsync in an un-awaited Task.Run. App.OnExit returns
        /// straight into process teardown, so that task never got far enough to put SHUTDOWN
        /// on the pipe: the worker saw its parent vanish, could not tell a deliberate exit
        /// from a crash, and stayed up polling hardware for the whole orphan timeout.
        ///
        /// The assertion is deliberately made with no waiting or polling after Dispose returns —
        /// that is the whole property under test.
        /// </summary>
        [Fact]
        public async Task Dispose_DeliversShutdown_BeforeReturning()
        {
            var (server, clientStream) = await CreateConnectedTestPipeAsync();
            using var serverDisposable = server;

            var (client, pipeField, _) = CreateClientWithReflectionAccess();
            pipeField.SetValue(client, clientStream);

            string? received = null;
            var serverTask = Task.Run(async () =>
            {
                var buffer = new byte[256];
                var bytesRead = await server.ReadAsync(buffer, 0, buffer.Length);
                received = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                var reply = Encoding.UTF8.GetBytes("OK");
                await server.WriteAsync(reply, 0, reply.Length);
                await server.FlushAsync();
            });

            await Task.Run(() => client.Dispose());

            received.Should().Be("SHUTDOWN",
                "Dispose must have delivered the shutdown request before returning — a caller that exits the process immediately afterwards gives a fire-and-forget task no chance to run");

            await serverTask;
        }

        /// <summary>
        /// The blocking shutdown must stay bounded: a wedged worker that never answers the pipe
        /// cannot be allowed to hang the app's exit. Failing to stop it is recoverable (the
        /// worker's own orphan watchdog still applies); an app that will not close is not.
        /// </summary>
        [Fact]
        public async Task Dispose_ReturnsWithinTheBound_WhenTheWorkerNeverAnswers()
        {
            var (server, clientStream) = await CreateConnectedTestPipeAsync();
            using var serverDisposable = server;

            var (client, pipeField, _) = CreateClientWithReflectionAccess();
            pipeField.SetValue(client, clientStream);

            // Server never reads or replies — the request times out inside SendRequestAsync.
            var stopwatch = Stopwatch.StartNew();
            await Task.Run(() => client.Dispose());
            stopwatch.Stop();

            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15),
                "the stop sequence is bounded by BlockingStopTimeoutMs and must not wait on an unresponsive worker indefinitely");
        }

        /// <summary>
        /// StopAsync resolves the worker by process name because the client usually did not
        /// launch it — but only if it ever attached to one. A client that never connected must
        /// not reach out and terminate whatever OmenCore.HardwareWorker happens to be running,
        /// which on a developer machine is a live worker belonging to the installed app.
        /// </summary>
        [Fact]
        public async Task StopAsync_DoesNotTouchAnyProcess_WhenTheClientNeverAttached()
        {
            var log = new List<string>();
            var client = new HardwareWorkerClient(log.Add);

            var attached = typeof(HardwareWorkerClient)
                .GetField("_attachedToWorker", BindingFlags.Instance | BindingFlags.NonPublic);
            attached.Should().NotBeNull();
            attached!.GetValue(client).Should().Be(false, "a freshly constructed client has not started or attached to a worker");

            await client.StopAsync();

            log.Should().NotContain(line => line.Contains("terminating", StringComparison.OrdinalIgnoreCase),
                "an unattached client must never terminate a worker process it does not own");
            log.Should().NotContain(line => line.Contains("Worker PID", StringComparison.Ordinal),
                "an unattached client must not even resolve a worker process");
        }
    }
}
