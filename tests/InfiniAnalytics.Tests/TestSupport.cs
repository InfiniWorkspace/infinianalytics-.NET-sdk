using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace InfiniAnalytics.Tests;

/// <summary>Request as seen by <see cref="FakeHttpMessageHandler"/>, captured before the SDK disposes it.</summary>
internal sealed class RecordedRequest
{
    public RecordedRequest(HttpMethod method, Uri uri, IReadOnlyDictionary<string, string> headers, string? contentType, string body)
    {
        Method = method;
        Uri = uri;
        Headers = headers;
        ContentType = contentType;
        Body = body;
    }

    public HttpMethod Method { get; }

    public Uri Uri { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public string? ContentType { get; }

    public string Body { get; }

    public JsonElement Json()
    {
        using var document = JsonDocument.Parse(Body);
        return document.RootElement.Clone();
    }

    /// <summary>JSON body as a flat dictionary of string properties, for exact comparisons.</summary>
    public Dictionary<string, string?> JsonProperties() =>
        Json().EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
}

/// <summary>Fake transport: no test ever reaches the real API.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
    private readonly List<RecordedRequest> _requests = new();

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToList();
            }
        }
    }

    public static FakeHttpMessageHandler Returning(HttpStatusCode status, string? body = null) =>
        new((_, _) => Task.FromResult(Response(status, body)));

    public static FakeHttpMessageHandler ReturningJson(object body, HttpStatusCode status = HttpStatusCode.Created) =>
        Returning(status, JsonSerializer.Serialize(body));

    public static FakeHttpMessageHandler Throwing(Exception exception) =>
        new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    public static FakeHttpMessageHandler NeverResponding() =>
        new(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Unreachable.");
        });

    public static HttpResponseMessage Response(HttpStatusCode status, string? body = null)
    {
        var response = new HttpResponseMessage(status);
        if (body is not null)
        {
            response.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
        lock (_requests)
        {
            _requests.Add(new RecordedRequest(
                request.Method, request.RequestUri!, headers, request.Content?.Headers.ContentType?.ToString(), body));
        }

        return await _respond(request, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Captures everything the SDK logs.</summary>
internal sealed class LogRecorder
{
    private readonly List<(InfiniAnalyticsLogLevel Level, string Message, Exception? Exception)> _entries = new();

    public IReadOnlyList<(InfiniAnalyticsLogLevel Level, string Message, Exception? Exception)> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    public void Log(InfiniAnalyticsLogLevel level, string message, Exception? exception)
    {
        lock (_entries)
        {
            _entries.Add((level, message, exception));
        }
    }

    /// <summary>All logged text, including exception details, as a default logger would print it.</summary>
    public string AllText() =>
        string.Join("\n", Entries.Select(e => $"{e.Message} {e.Exception}"));
}

internal static class TestClient
{
    public const string Token = "test-token-placeholder";
    public const string AutomationId = "44444444-4444-4444-4444-444444444444";

    public static InfiniAnalyticsClient Create(
        FakeHttpMessageHandler handler,
        LogRecorder? log = null,
        Action<InfiniAnalyticsClientOptions>? configure = null)
    {
        var options = new InfiniAnalyticsClientOptions
        {
            Token = Token,
            Logger = (log ?? new LogRecorder()).Log,
        };
        configure?.Invoke(options);
        return new InfiniAnalyticsClient(new HttpClient(handler), options);
    }

    public static object StoredEvent(string type = "START", string description = "") => new Dictionary<string, object?>
    {
        ["automation"] = AutomationId,
        ["execution_id"] = "exec-1",
        ["environment"] = 1,
        ["type_of_event"] = type,
        ["description"] = description,
        ["error_id"] = "",
        ["error_description_detail"] = "",
        ["created_at"] = "2025-03-03T16:00:29.998530Z",
    };
}
