using System;
using System.Net.Http;
using System.Threading;

namespace InfiniAnalytics.Internal;

/// <summary>
/// Process-wide <see cref="HttpClient"/> used when none is injected, so creating many
/// clients (e.g. one per UiPath Invoke Code) does not exhaust sockets. Timeouts are applied
/// per request by <see cref="InfiniAnalyticsClient"/>, so this client has none.
/// </summary>
internal static class SharedHttpClient
{
    private static readonly Lazy<HttpClient> s_instance = new(Create);

    public static HttpClient Instance => s_instance.Value;

    private static HttpClient Create()
    {
#if NET
        // Recycle pooled connections so DNS changes are picked up in long-running processes.
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
#else
        return new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
#endif
    }
}
