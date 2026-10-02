using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using InfiniAnalytics.Internal;

namespace InfiniAnalytics;

/// <summary>
/// Client for the InfiniAnalytics ingestion API. Thread-safe; create one and reuse it.
/// </summary>
/// <remarks>
/// Never throws on a failed API call: a communication error, a timeout or a non-2xx
/// response is logged and the call returns <see langword="null"/> (or
/// <see langword="false"/> for <see cref="PingAsync"/>), so it never breaks the
/// automation's control flow. Misuse of the SDK itself (e.g. an empty token) throws from
/// the constructor, and cancelling a caller-supplied <see cref="CancellationToken"/>
/// throws <see cref="OperationCanceledException"/>.
/// </remarks>
public sealed class InfiniAnalyticsClient
{
    private const string RegisterPath = "/v1/register/";

    // TODO: switch to "/v1/ping/" (with the token header) once the backend implements it.
    private const string PingPath = "/health";

    private const int MaxLoggedBodyLength = 1000;

    private readonly HttpClient _httpClient;
    private readonly string _token;
    private readonly string _baseUrl;
    private readonly TimeSpan _timeout;
    private readonly Action<InfiniAnalyticsLogLevel, string, Exception?> _logger;

    /// <summary>Creates a client for the production API.</summary>
    /// <param name="token">Organization token, from the InfiniAnalytics dashboard.</param>
    /// <exception cref="ArgumentException"><paramref name="token"/> is empty.</exception>
    public InfiniAnalyticsClient(string token)
        : this(new InfiniAnalyticsClientOptions { Token = token })
    {
    }

    /// <summary>Creates a client that uses a shared, process-wide <see cref="HttpClient"/>.</summary>
    /// <param name="options">Client configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The token is empty or the base URL is not an absolute http(s) URL.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The timeout is not positive.</exception>
    public InfiniAnalyticsClient(InfiniAnalyticsClientOptions options)
        : this(SharedHttpClient.Instance, options)
    {
    }

    /// <summary>
    /// Creates a client that sends requests through <paramref name="httpClient"/>, e.g. one
    /// provided by <c>IHttpClientFactory</c>. The client is not disposed by this class and its
    /// <see cref="HttpClient.BaseAddress"/> is ignored in favor of
    /// <see cref="InfiniAnalyticsClientOptions.BaseUrl"/>.
    /// </summary>
    /// <param name="httpClient">HTTP client used to send requests.</param>
    /// <param name="options">Client configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The token is empty or the base URL is not an absolute http(s) URL.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The timeout is not positive.</exception>
    public InfiniAnalyticsClient(HttpClient httpClient, InfiniAnalyticsClientOptions options)
    {
        if (httpClient is null)
        {
            throw new ArgumentNullException(nameof(httpClient));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var token = options.Token?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("InfiniAnalyticsClient requires a non-empty token.", nameof(options));
        }

        var baseUrl = (options.BaseUrl ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException(
                $"InfiniAnalyticsClient requires an absolute http(s) base URL, got '{options.BaseUrl}'.", nameof(options));
        }

        if (options.Timeout <= TimeSpan.Zero && options.Timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.Timeout, "InfiniAnalyticsClient requires a positive timeout.");
        }

        _httpClient = httpClient;
        _token = token!;
        _baseUrl = baseUrl;
        _timeout = options.Timeout;
        _logger = options.Logger ?? WriteToConsole;
    }

    /// <summary>
    /// Checks that the API is reachable. Returns <see langword="true"/> on a 2xx response and
    /// <see langword="false"/> otherwise; never throws for communication failures.
    /// </summary>
    /// <remarks>
    /// Currently calls <c>GET /health</c>, which confirms the API is reachable but does not
    /// validate the token. It will call <c>GET /v1/ping/</c>, which also validates the token,
    /// once the backend implements it.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + PingPath);
        var response = await SendAsync(request, "pinging", cancellationToken).ConfigureAwait(false);
        return response is { IsSuccess: true };
    }

    /// <summary>Synchronous version of <see cref="PingAsync"/>, safe to call from any thread.</summary>
    public bool Ping() => SyncRunner.Run(() => PingAsync());

    /// <summary>
    /// Registers the START event of a new execution and returns an <see cref="Execution"/> used
    /// to report its following events. Always returns an <see cref="Execution"/>, even if the
    /// START call fails, so the automation can keep calling its methods.
    /// </summary>
    /// <param name="automationId">UUID of the automation in the InfiniAnalytics platform.</param>
    /// <param name="executionId">
    /// Identifier of this execution; must not be reused by a concurrent execution of the same
    /// automation. When <see langword="null"/> or empty, the current UTC time is used in ISO 8601
    /// format (e.g. <c>2026-10-02T15:04:05.123Z</c>).
    /// </param>
    /// <param name="description">Optional description of the START event.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    public async Task<Execution> StartExecutionAsync(
        string automationId,
        string? executionId = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var execution = new Execution(
            this,
            automationId,
            string.IsNullOrWhiteSpace(executionId) ? CreateExecutionId() : executionId!);

        await RegisterAsync(
            new RegisterEventPayload
            {
                AutomationId = execution.AutomationId,
                ExecutionId = execution.ExecutionId,
                Event = EventType.Start,
                Description = description,
            },
            cancellationToken).ConfigureAwait(false);

        return execution;
    }

    /// <summary>
    /// Synchronous version of <see cref="StartExecutionAsync"/>, safe to call from any thread
    /// (UiPath Invoke Code, Blue Prism code stages, UI threads).
    /// </summary>
    /// <param name="automationId">UUID of the automation in the InfiniAnalytics platform.</param>
    /// <param name="executionId">Identifier of this execution; generated when <see langword="null"/> or empty.</param>
    /// <param name="description">Optional description of the START event.</param>
    public Execution StartExecution(string automationId, string? executionId = null, string? description = null) =>
        SyncRunner.Run(() => StartExecutionAsync(automationId, executionId, description));

    /// <summary>
    /// Low-level call to <c>POST /v1/register/</c>. Prefer <see cref="StartExecutionAsync"/> and the
    /// <see cref="Execution"/> methods unless you need full control over the payload.
    /// </summary>
    /// <remarks>
    /// Never throws for communication failures: returns <see langword="null"/> after logging a
    /// communication error, a timeout or a non-2xx response. Texts longer than the API limits are
    /// truncated (with a warning) so the event is not rejected.
    /// </remarks>
    /// <param name="payload">Event to register.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The event as stored by the API, or <see langword="null"/> if it could not be registered.</returns>
    public async Task<ExecutionEventResult?> RegisterAsync(
        RegisterEventPayload payload, CancellationToken cancellationToken = default)
    {
        if (payload is null)
        {
            Log(InfiniAnalyticsLogLevel.Error, "Failed to register an event: the payload is null.");
            return null;
        }

        if (!EventTypeNames.TryToWire(payload.Event, out var eventName))
        {
            Log(InfiniAnalyticsLogLevel.Error, $"Failed to register an event: '{payload.Event}' is not a valid event type.");
            return null;
        }

        var body = PayloadWriter.Write(
            payload,
            eventName,
            (field, max, length) => Log(
                InfiniAnalyticsLogLevel.Warning,
                $"'{field}' of a '{eventName}' event has {length} characters and was truncated to the API limit of {max}."));

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + RegisterPath)
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation("token", _token);

        var response = await SendAsync(request, $"registering a '{eventName}' event", cancellationToken)
            .ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        if (!response.IsSuccess)
        {
            Log(
                InfiniAnalyticsLogLevel.Error,
                $"Failed to register a '{eventName}' event (API responded with {response.StatusCode} {response.ReasonPhrase}): "
                + Shorten(response.Body));
            return null;
        }

        return ResponseReader.Read(response.Body);
    }

    /// <summary>Synchronous version of <see cref="RegisterAsync"/>, safe to call from any thread.</summary>
    /// <param name="payload">Event to register.</param>
    /// <returns>The event as stored by the API, or <see langword="null"/> if it could not be registered.</returns>
    public ExecutionEventResult? Register(RegisterEventPayload payload) => SyncRunner.Run(() => RegisterAsync(payload));

    private async Task<ApiResponse?> SendAsync(HttpRequestMessage request, string action, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_timeout != Timeout.InfiniteTimeSpan)
        {
            timeoutSource.CancelAfter(_timeout);
        }

        try
        {
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutSource.Token)
                .ConfigureAwait(false);
            var body = response.Content is null
                ? ""
                : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            return new ApiResponse((int)response.StatusCode, response.ReasonPhrase, response.IsSuccessStatusCode, body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            Log(InfiniAnalyticsLogLevel.Error, $"Failed to reach the API while {action}: the request timed out.", ex);
            return null;
        }
        catch (Exception ex)
        {
            Log(InfiniAnalyticsLogLevel.Error, $"Failed to reach the API while {action}.", ex);
            return null;
        }
    }

    private void Log(InfiniAnalyticsLogLevel level, string message, Exception? exception = null)
    {
        try
        {
            _logger(level, message, exception);
        }
        catch
        {
            // A faulty logger must never break the automation.
        }
    }

    private static void WriteToConsole(InfiniAnalyticsLogLevel level, string message, Exception? exception)
    {
        var line = $"[InfiniAnalytics] {level}: {message}";
        if (exception is not null)
        {
            line += $" {exception.GetType().Name}: {exception.Message}";
            var root = exception.GetBaseException();
            if (!ReferenceEquals(root, exception))
            {
                line += $" ({root.GetType().Name}: {root.Message})";
            }
        }

        Console.Error.WriteLine(line);
    }

    private static string CreateExecutionId() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Shorten(string text) =>
        text.Length <= MaxLoggedBodyLength ? text : text.Substring(0, MaxLoggedBodyLength) + "...";

    private sealed class ApiResponse
    {
        public ApiResponse(int statusCode, string? reasonPhrase, bool isSuccess, string body)
        {
            StatusCode = statusCode;
            ReasonPhrase = reasonPhrase;
            IsSuccess = isSuccess;
            Body = body;
        }

        public int StatusCode { get; }

        public string? ReasonPhrase { get; }

        public bool IsSuccess { get; }

        public string Body { get; }
    }
}
