namespace InfiniAnalytics;

/// <summary>Severity of a message written through <see cref="InfiniAnalyticsClientOptions.Logger"/>.</summary>
public enum InfiniAnalyticsLogLevel
{
    /// <summary>Something was adjusted but the event was still sent (e.g. a text was truncated).</summary>
    Warning,

    /// <summary>An API call failed; the method returned <see langword="null"/> or <see langword="false"/>.</summary>
    Error,
}
