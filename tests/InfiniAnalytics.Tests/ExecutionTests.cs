using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace InfiniAnalytics.Tests;

public class ExecutionTests
{
    private static async Task<(Execution Execution, FakeHttpMessageHandler Handler)> StartAsync()
    {
        var handler = FakeHttpMessageHandler.ReturningJson(TestClient.StoredEvent());
        var execution = await TestClient.Create(handler).StartExecutionAsync(TestClient.AutomationId, "exec-1");
        return (execution, handler);
    }

    [Fact]
    public async Task Event_warning_and_end_reuse_the_automation_and_execution_ids()
    {
        var (execution, handler) = await StartAsync();

        await execution.EventAsync("step 1");
        await execution.WarningAsync("low disk");
        await execution.EndAsync("done");

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(Expected("EVENT", "step 1"), handler.Requests[1].JsonProperties());
        Assert.Equal(Expected("WARNING", "low disk"), handler.Requests[2].JsonProperties());
        Assert.Equal(Expected("END", "done"), handler.Requests[3].JsonProperties());
    }

    [Fact]
    public async Task Methods_can_be_called_without_a_description()
    {
        var (execution, handler) = await StartAsync();

        await execution.EventAsync();
        await execution.EndAsync();

        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["automation_id"] = TestClient.AutomationId,
                ["execution_id"] = "exec-1",
                ["event"] = "END",
            },
            handler.Requests[2].JsonProperties());
    }

    [Fact]
    public async Task ErrorAsync_forwards_an_explicit_error_id_and_description()
    {
        var (execution, handler) = await StartAsync();

        await execution.ErrorAsync("High-level error", errorId: "e0001", errorDescription: "Detailed error");

        var expected = Expected("ERROR", "High-level error");
        expected["error_id"] = "e0001";
        expected["error_description"] = "Detailed error";
        Assert.Equal(expected, handler.Requests[1].JsonProperties());
    }

    [Fact]
    public async Task ErrorAsync_derives_the_error_description_from_a_caught_exception()
    {
        var (execution, handler) = await StartAsync();

        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception ex)
        {
            await execution.ErrorAsync("Failed", ex);
        }

        var body = handler.Requests[1].JsonProperties();
        Assert.Equal("ERROR", body["event"]);
        Assert.Equal("Failed", body["description"]);
        Assert.Equal("InvalidOperationException: boom", body["error_description"]);
    }

    [Fact]
    public async Task ErrorAsync_prefers_an_explicit_error_description_over_the_derived_one()
    {
        var (execution, handler) = await StartAsync();

        await execution.ErrorAsync(exception: new Exception("ignored"), errorDescription: "custom");

        Assert.Equal("custom", handler.Requests[1].JsonProperties()["error_description"]);
    }

    [Fact]
    public async Task ErrorAsync_without_an_exception_omits_the_error_description()
    {
        var (execution, handler) = await StartAsync();

        await execution.ErrorAsync("Failed");

        Assert.False(handler.Requests[1].JsonProperties().ContainsKey("error_description"));
    }

    [Fact]
    public async Task Methods_return_null_instead_of_throwing_when_the_api_call_fails()
    {
        var log = new LogRecorder();
        var client = TestClient.Create(FakeHttpMessageHandler.Returning(HttpStatusCode.InternalServerError), log);
        var execution = await client.StartExecutionAsync(TestClient.AutomationId, "exec-1");

        Assert.Null(await execution.EventAsync("step 1"));
        Assert.Null(await execution.WarningAsync("warning"));
        Assert.Null(await execution.ErrorAsync("error", new Exception("boom")));
        Assert.Null(await execution.EndAsync("done"));
        Assert.Equal(5, log.Entries.Count);
    }

    [Fact]
    public async Task Methods_return_the_stored_event_on_success()
    {
        var handler = FakeHttpMessageHandler.ReturningJson(TestClient.StoredEvent("END", "done"));
        var execution = new Execution(TestClient.Create(handler), TestClient.AutomationId, "exec-1");

        var result = await execution.EndAsync("done");

        Assert.Equal(EventType.End, result!.EventType);
        Assert.Equal("done", result.Description);
    }

    [Fact]
    public async Task Constructor_resumes_an_execution_without_sending_a_start_event()
    {
        var handler = FakeHttpMessageHandler.ReturningJson(TestClient.StoredEvent());
        var execution = new Execution(TestClient.Create(handler), TestClient.AutomationId, "exec-1");

        Assert.Empty(handler.Requests);

        await execution.EndAsync("done");

        Assert.Equal(Expected("END", "done"), Assert.Single(handler.Requests).JsonProperties());
    }

    [Fact]
    public void Constructor_throws_without_a_client()
    {
        Assert.Throws<ArgumentNullException>(() => new Execution(null!, TestClient.AutomationId, "exec-1"));
    }

    private static Dictionary<string, string?> Expected(string eventName, string description) => new()
    {
        ["automation_id"] = TestClient.AutomationId,
        ["execution_id"] = "exec-1",
        ["event"] = eventName,
        ["description"] = description,
    };
}
