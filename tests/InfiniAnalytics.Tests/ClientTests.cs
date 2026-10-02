using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace InfiniAnalytics.Tests;

public class ClientTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_throws_without_a_token(string? token)
    {
        var ex = Assert.Throws<ArgumentException>(() => new InfiniAnalyticsClient(token!));
        Assert.Contains("token", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_throws_on_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => new InfiniAnalyticsClient((InfiniAnalyticsClientOptions)null!));
        Assert.Throws<ArgumentNullException>(() =>
            new InfiniAnalyticsClient(null!, new InfiniAnalyticsClientOptions { Token = TestClient.Token }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/relative")]
    [InlineData("ftp://api.analytics.infini.es")]
    public void Constructor_throws_on_an_invalid_base_url(string baseUrl)
    {
        Assert.Throws<ArgumentException>(() =>
            new InfiniAnalyticsClient(new InfiniAnalyticsClientOptions { Token = TestClient.Token, BaseUrl = baseUrl }));
    }

    [Fact]
    public void Constructor_throws_on_a_non_positive_timeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InfiniAnalyticsClient(new InfiniAnalyticsClientOptions { Token = TestClient.Token, Timeout = TimeSpan.Zero }));
    }

    [Fact]
    public async Task RegisterAsync_sends_the_token_header_and_the_json_body()
    {
        var handler = FakeHttpMessageHandler.ReturningJson(TestClient.StoredEvent(description: "Starting"));
        var client = TestClient.Create(handler);

        var result = await client.RegisterAsync(new RegisterEventPayload
        {
            AutomationId = TestClient.AutomationId,
            ExecutionId = "exec-1",
            Event = EventType.Start,
            Description = "Starting",
        });

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.analytics.infini.es/v1/register/", request.Uri.ToString());
        Assert.Equal(TestClient.Token, request.Headers["token"]);
        Assert.False(request.Headers.ContainsKey("Authorization"));
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["automation_id"] = TestClient.AutomationId,
                ["execution_id"] = "exec-1",
                ["event"] = "START",
                ["description"] = "Starting",
            },
            request.JsonProperties());

        Assert.NotNull(result);
        Assert.Equal(TestClient.AutomationId, result!.AutomationId);
        Assert.Equal("exec-1", result.ExecutionId);
        Assert.Equal(EventType.Start, result.EventType);
        Assert.Equal("Starting", result.Description);
        Assert.Equal("", result.ErrorId);
        Assert.Equal("", result.ErrorDescription);
        Assert.Equal(1, result.Environment);
        Assert.Equal(new DateTimeOffset(2025, 3, 3, 16, 0, 29, TimeSpan.Zero).AddTicks(9985300), result.CreatedAt);
    }

    [Theory]
    [InlineData(EventType.Start, "START")]
    [InlineData(EventType.Event, "EVENT")]
    [InlineData(EventType.Warning, "WARNING")]
    [InlineData(EventType.Error, "ERROR")]
    [InlineData(EventType.End, "END")]
    public async Task RegisterAsync_sends_event_types_in_upper_case(EventType eventType, string expected)
    {
        var handler = FakeHttpMessageHandler.ReturningJson(TestClient.StoredEvent());
        var client = TestClient.Create(handler);

        await client.RegisterAsync(new RegisterEventPayload { AutomationId = "a", ExecutionId = "e", Event = eventType });

        Assert.Equal(expected, handler.Requests[0].Json().GetProperty("event").GetString());
    }

    [Fact]
    public async Task RegisterAsync_maps_error_description_detail_from_the_response()
    {
        var handler = FakeHttpMessageHandler.ReturningJson(new Dictionary<string, object?>
        {
            ["type_of_event"] = "ERROR",
            ["error_id"] = "E001",
            ["error_description_detail"] = "Detailed error",
        });
        var client = TestClient.Create(handler);

        var result = await client.RegisterAsync(new RegisterEventPayload
        {
            AutomationId = "a",
            ExecutionId = "e",
            Event = EventType.Error,
            ErrorId = "E001",
            ErrorDescription = "Detailed error",
        });

        Assert.Equal("Detailed error", handler.Requests[0].Json().GetProperty("error_description").GetString());
        Assert.Equal("E001", result!.ErrorId);
        Assert.Equal("Detailed error", result.ErrorDescription);
    }

    [Fact]
    public async Task RegisterAsync_omits_null_optional_fields()
    {
        var handler = FakeHttpMessageHandler.ReturningJson(TestClient.StoredEvent());
        var client = TestClient.Create(handler);

        await client.RegisterAsync(new RegisterEventPayload { AutomationId = "a", ExecutionId = "e", Event = EventType.End });

        Assert.Equal(
            new[] { "automation_id", "execution_id", "event" },
            handler.Requests[0].Json().EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task RegisterAsync_uses_a_custom_base_url_without_trailing_slashes()
    {
        var handler = FakeHttpMessageHandler.ReturningJson(TestClient.StoredEvent());
        var client = TestClient.Create(handler, configure: o => o.BaseUrl = "https://staging.example.com//");

        await client.RegisterAsync(new RegisterEventPayload { AutomationId = "a", ExecutionId = "e", Event = EventType.Start });

        Assert.Equal("https://staging.example.com/v1/register/", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task RegisterAsync_trims_the_token()
    {
        var handler = FakeHttpMessageHandler.ReturningJson(TestClient.StoredEvent());
        var client = TestClient.Create(handler, configure: o => o.Token = "  " + TestClient.Token + "\r\n");

        await client.RegisterAsync(new RegisterEventPayload { AutomationId = "a", ExecutionId = "e", Event = EventType.Start });

        Assert.Equal(TestClient.Token, handler.Requests[0].Headers["token"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{\"detail\":\"Invalid token\"}")]
    [InlineData(HttpStatusCode.BadRequest, "{\"detail\":\"Automation does not exist\"}")]
    [InlineData((HttpStatusCode)422, "{\"detail\":[{\"msg\":\"too long\"}]}")]
    [InlineData(HttpStatusCode.InternalServerError, null)]
    [InlineData(HttpStatusCode.BadGateway, "<html>Bad gateway</html>")]
    public async Task RegisterAsync_returns_null_and_logs_on_a_non_2xx_response(HttpStatusCode status, string? body)
    {
        var log = new LogRecorder();
        var client = TestClient.Create(FakeHttpMessageHandler.Returning(status, body), log);

        var result = await client.RegisterAsync(new RegisterEventPayload { AutomationId = "a", ExecutionId = "e", Event = EventType.Start });

        Assert.Null(result);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(InfiniAnalyticsLogLevel.Error, entry.Level);
        Assert.Contains(((int)status).ToString(), entry.Message);
        Assert.Contains("'START'", entry.Message);
        if (body is not null)
        {
            Assert.Contains(body, entry.Message);
        }
    }

    [Fact]
    public async Task RegisterAsync_returns_null_and_logs_on_a_network_error()
    {
        var log = new LogRecorder();
        var error = new HttpRequestException("No such host is known.");
        var client = TestClient.Create(FakeHttpMessageHandler.Throwing(error), log);

        var result = await client.RegisterAsync(new RegisterEventPayload { AutomationId = "a", ExecutionId = "e", Event = EventType.Event });

        Assert.Null(result);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(InfiniAnalyticsLogLevel.Error, entry.Level);
        Assert.Contains("'EVENT'", entry.Message);
        Assert.Same(error, entry.Exception);
    }

    [Fact]
    public async Task RegisterAsync_returns_null_and_logs_when_the_api_does_not_respond_in_time()
    {
        var log = new LogRecorder();
        var client = TestClient.Create(
            FakeHttpMessageHandler.NeverResponding(), log, o => o.Timeout = TimeSpan.FromMilliseconds(100));

        var result = await client.RegisterAsync(new RegisterEventPayload { AutomationId = "a", ExecutionId = "e", Event = EventType.Start });

        Assert.Null(result);
        var entry = Assert.Single(log.Entries);
        Assert.Contains("timed out", entry.Message);
    }

    [Fact]
    public async Task RegisterAsync_propagates_cancellation_requested_by_the_caller()
    {
        var log = new LogRecorder();
        var client = TestClient.Create(FakeHttpMessageHandler.NeverResponding(), log);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RegisterAsync(
            new RegisterEventPayload { AutomationId = "a", ExecutionId = "e", Event = EventType.Start },
            cancellation.Token));
        Assert.Empty(log.Entries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    public async Task RegisterAsync_returns_an_empty_result_on_a_2xx_response_without_a_json_object(string? body)
    {
        var client = TestClient.Create(FakeHttpMessageHandler.Returning(HttpStatusCode.Created, body));

        var result = await client.RegisterAsync(new RegisterEventPayload { AutomationId = "a", ExecutionId = "e", Event = EventType.Start });

        Assert.NotNull(result);
        Assert.Equal("", result!.AutomationId);
        Assert.Equal("", result.ExecutionId);
        Assert.Null(result.EventType);
        Assert.Equal("", result.Description);
        Assert.Null(result.CreatedAt);
        Assert.Null(result.Environment);
    }

    [Fact]
    public async Task RegisterAsync_tolerates_unknown_and_null_values_in_the_response()
    {
        var client = TestClient.Create(FakeHttpMessageHandler.ReturningJson(new Dictionary<string, object?>
        {
            ["type_of_event"] = "SOMETHING_NEW",
            ["description"] = null,
            ["error_id"] = 42,
            ["created_at"] = "not a date",
            ["environment"] = "3",
        }));

        var result = await client.RegisterAsync(new RegisterEventPayload { AutomationId = "a", ExecutionId = "e", Event = EventType.Start });

        Assert.Null(result!.EventType);
        Assert.Equal("", result.Description);
        Assert.Equal("42", result.ErrorId);
        Assert.Null(result.CreatedAt);
        Assert.Equal(3, result.Environment);
    }

    [Fact]
    public async Task RegisterAsync_returns_null_without_calling_the_api_for_an_invalid_event_type()
    {
        var log = new LogRecorder();
        var handler = FakeHttpMessageHandler.ReturningJson(TestClient.StoredEvent());
        var client = TestClient.Create(handler, log);

        var result = await client.RegisterAsync(new RegisterEventPayload { AutomationId = "a", ExecutionId = "e", Event = (EventType)42 });

        Assert.Null(result);
        Assert.Empty(handler.Requests);
        Assert.Equal(InfiniAnalyticsLogLevel.Error, Assert.Single(log.Entries).Level);
    }

    [Fact]
    public async Task RegisterAsync_returns_null_for_a_null_payload()
    {
        var log = new LogRecorder();
        var client = TestClient.Create(FakeHttpMessageHandler.ReturningJson(TestClient.StoredEvent()), log);

        Assert.Null(await client.RegisterAsync(null!));
        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task RegisterAsync_truncates_texts_to_the_api_limits_and_logs_a_warning()
    {
        var log = new LogRecorder();
        var handler = FakeHttpMessageHandler.ReturningJson(TestClient.StoredEvent());
        var client = TestClient.Create(handler, log);

        var result = await client.RegisterAsync(new RegisterEventPayload
        {
            AutomationId = "a",
            ExecutionId = "e",
            Event = EventType.Error,
            Description = new string('d', 2001),
            ErrorId = new string('i', 300),
            ErrorDescription = new string('x', 9000),
        });

        Assert.NotNull(result);
        var body = handler.Requests[0].Json();
        Assert.Equal(2000, body.GetProperty("description").GetString()!.Length);
        Assert.Equal(255, body.GetProperty("error_id").GetString()!.Length);
        Assert.Equal(5000, body.GetProperty("error_description").GetString()!.Length);
        Assert.Equal(3, log.Entries.Count);
        Assert.All(log.Entries, e => Assert.Equal(InfiniAnalyticsLogLevel.Warning, e.Level));
        Assert.Contains(log.Entries, e => e.Message.Contains("'description'") && e.Message.Contains("2001"));
    }

    [Fact]
    public async Task RegisterAsync_does_not_truncate_texts_at_the_exact_limit()
    {
        var log = new LogRecorder();
        var handler = FakeHttpMessageHandler.ReturningJson(TestClient.StoredEvent());
        var client = TestClient.Create(handler, log);
        var description = new string('d', 2000);

        await client.RegisterAsync(new RegisterEventPayload { AutomationId = "a", ExecutionId = "e", Event = EventType.Event, Description = description });

        Assert.Equal(description, handler.Requests[0].Json().GetProperty("description").GetString());
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task RegisterAsync_never_splits_a_surrogate_pair_when_truncating()
    {
        var handler = FakeHttpMessageHandler.ReturningJson(TestClient.StoredEvent());
        var client = TestClient.Create(handler);
        // 1999 ASCII chars followed by an emoji (2 UTF-16 code units) straddling the limit.
        var description = new string('d', 1999) + char.ConvertFromUtf32(0x1F600) + "tail";

        await client.RegisterAsync(new RegisterEventPayload { AutomationId = "a", ExecutionId = "e", Event = EventType.Event, Description = description });

        Assert.Equal(new string('d', 1999), handler.Requests[0].Json().GetProperty("description").GetString());
    }

    [Fact]
    public async Task The_token_is_never_logged()
    {
        var log = new LogRecorder();
        var payload = new RegisterEventPayload
        {
            AutomationId = "a",
            ExecutionId = "e",
            Event = EventType.Start,
            Description = new string('d', 3000),
        };

        await TestClient.Create(FakeHttpMessageHandler.Returning(HttpStatusCode.Unauthorized, "{\"detail\":\"Invalid token\"}"), log)
            .RegisterAsync(payload);
        await TestClient.Create(FakeHttpMessageHandler.Throwing(new HttpRequestException("boom")), log).RegisterAsync(payload);
        await TestClient.Create(FakeHttpMessageHandler.NeverResponding(), log, o => o.Timeout = TimeSpan.FromMilliseconds(50))
            .RegisterAsync(payload);
        await TestClient.Create(FakeHttpMessageHandler.Throwing(new HttpRequestException("boom")), log).PingAsync();

        Assert.NotEmpty(log.Entries);
        Assert.DoesNotContain(TestClient.Token, log.AllText());
    }

    [Fact]
    public async Task A_failing_logger_never_breaks_the_call()
    {
        var client = new InfiniAnalyticsClient(
            new HttpClient(FakeHttpMessageHandler.Returning(HttpStatusCode.InternalServerError)),
            new InfiniAnalyticsClientOptions
            {
                Token = TestClient.Token,
                Logger = (_, _, _) => throw new InvalidOperationException("logger failure"),
            });

        var result = await client.RegisterAsync(new RegisterEventPayload { AutomationId = "a", ExecutionId = "e", Event = EventType.Start });

        Assert.Null(result);
    }

    [Fact]
    public async Task StartExecutionAsync_registers_a_start_event_and_returns_an_execution()
    {
        var handler = FakeHttpMessageHandler.ReturningJson(TestClient.StoredEvent());
        var client = TestClient.Create(handler);

        var execution = await client.StartExecutionAsync(TestClient.AutomationId, "exec-1", "Starting");

        Assert.Equal(TestClient.AutomationId, execution.AutomationId);
        Assert.Equal("exec-1", execution.ExecutionId);
        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["automation_id"] = TestClient.AutomationId,
                ["execution_id"] = "exec-1",
                ["event"] = "START",
                ["description"] = "Starting",
            },
            Assert.Single(handler.Requests).JsonProperties());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task StartExecutionAsync_generates_an_iso_8601_execution_id_when_none_is_given(string? executionId)
    {
        var handler = FakeHttpMessageHandler.ReturningJson(new { });
        var client = TestClient.Create(handler);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var execution = await client.StartExecutionAsync(TestClient.AutomationId, executionId);

        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$"), execution.ExecutionId);
        Assert.InRange(DateTimeOffset.Parse(execution.ExecutionId), before, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(execution.ExecutionId, handler.Requests[0].Json().GetProperty("execution_id").GetString());
    }

    [Fact]
    public async Task StartExecutionAsync_still_returns_an_execution_if_the_start_call_fails()
    {
        var client = TestClient.Create(FakeHttpMessageHandler.Returning(HttpStatusCode.InternalServerError));

        var execution = await client.StartExecutionAsync(TestClient.AutomationId, "exec-1");

        Assert.Equal(TestClient.AutomationId, execution.AutomationId);
        Assert.Equal("exec-1", execution.ExecutionId);
    }

    [Fact]
    public async Task PingAsync_calls_health_and_returns_true_when_the_api_responds_ok()
    {
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, "{\"status\":\"ok\"}");
        var client = TestClient.Create(handler);

        Assert.True(await client.PingAsync());
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.analytics.infini.es/health", request.Uri.ToString());
        Assert.False(request.Headers.ContainsKey("token"));
    }

    [Fact]
    public async Task PingAsync_returns_false_when_the_api_responds_with_an_error_status()
    {
        var client = TestClient.Create(FakeHttpMessageHandler.Returning(HttpStatusCode.ServiceUnavailable));

        Assert.False(await client.PingAsync());
    }

    [Fact]
    public async Task PingAsync_returns_false_and_logs_on_a_network_error()
    {
        var log = new LogRecorder();
        var client = TestClient.Create(FakeHttpMessageHandler.Throwing(new HttpRequestException("boom")), log);

        Assert.False(await client.PingAsync());
        Assert.Single(log.Entries);
    }
}
