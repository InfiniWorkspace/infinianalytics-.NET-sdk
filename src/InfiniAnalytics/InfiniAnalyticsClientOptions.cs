using System;

namespace InfiniAnalytics;

/// <summary>Configuration for <see cref="InfiniAnalyticsClient"/>.</summary>
public sealed class InfiniAnalyticsClientOptions
{
    /// <summary>Production API base URL.</summary>
    public const string DefaultBaseUrl = "https://api.analytics.infini.es";

    /// <summary>Organization token, from the InfiniAnalytics dashboard. Required.</summary>
    public string Token { get; set; } = "";

    /// <summary>API base URL. Defaults to <see cref="DefaultBaseUrl"/>.</summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>
    /// Maximum time for each API call, so an automation never hangs if the API does not
    /// respond. Defaults to 10 seconds. When it elapses the call is logged and returns
    /// <see langword="null"/>. Use <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to disable it.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Receives the SDK's diagnostic messages. Defaults to writing to the standard error
    /// output (<see cref="Console.Error"/>). Messages never contain the token. Exceptions
    /// thrown by the logger are ignored.
    /// </summary>
    public Action<InfiniAnalyticsLogLevel, string, Exception?>? Logger { get; set; }
}
