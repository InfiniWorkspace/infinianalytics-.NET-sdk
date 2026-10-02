using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace InfiniAnalytics.Tests;

public class SyncApiTests
{
    [Fact]
    public void Sync_methods_register_every_event()
    {
        var handler = FakeHttpMessageHandler.ReturningJson(TestClient.StoredEvent());
        var client = TestClient.Create(handler);

        var execution = client.StartExecution(TestClient.AutomationId, "exec-1", "Starting");
        Assert.NotNull(execution.Event("step"));
        Assert.NotNull(execution.Warning("warning"));
        Assert.NotNull(execution.Error("error", new InvalidOperationException("boom"), "E001"));
        Assert.NotNull(execution.End("done"));
        Assert.NotNull(client.Register(new RegisterEventPayload { AutomationId = "a", ExecutionId = "e", Event = EventType.Event }));
        Assert.True(client.Ping());

        Assert.Equal(7, handler.Requests.Count);
        Assert.Equal("InvalidOperationException: boom", handler.Requests[3].JsonProperties()["error_description"]);
    }

    [Fact]
    public void Sync_methods_return_null_instead_of_throwing_on_failures()
    {
        var client = TestClient.Create(FakeHttpMessageHandler.Throwing(new HttpRequestException("boom")));

        var execution = client.StartExecution(TestClient.AutomationId, "exec-1");

        Assert.Equal("exec-1", execution.ExecutionId);
        Assert.Null(execution.Event("step"));
        Assert.Null(execution.End("done"));
        Assert.False(client.Ping());
    }

    [Fact]
    public void Sync_methods_do_not_deadlock_on_a_single_threaded_synchronization_context()
    {
        // Simulates a UI or robot thread (WinForms, WPF, UiPath): a context that only runs
        // continuations on its own thread, which is blocked by the synchronous call.
        var handler = new FakeHttpMessageHandler(async (_, _) =>
        {
            await Task.Delay(20);
            return FakeHttpMessageHandler.Response(HttpStatusCode.Created, "{}");
        });
        var client = TestClient.Create(handler);
        Exception? failure = null;
        var completed = false;

        var thread = new Thread(() =>
        {
            var context = new SingleThreadSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var execution = client.StartExecution(TestClient.AutomationId, "exec-1");
                execution.Event("step");
                execution.End("done");
                completed = true;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The synchronous calls deadlocked.");
        Assert.Null(failure);
        Assert.True(completed);
        Assert.Equal(3, handler.Requests.Count);
    }

    /// <summary>
    /// Queues posted callbacks but never runs them unless the owning thread pumps them,
    /// which it cannot do while blocked: any continuation captured on it would deadlock.
    /// </summary>
    private sealed class SingleThreadSynchronizationContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();
    }
}
