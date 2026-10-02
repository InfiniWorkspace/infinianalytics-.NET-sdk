using System;

namespace InfiniAnalytics.Internal;

internal static class ErrorDescriber
{
    /// <summary>
    /// Formats a caught exception as <c>"&lt;Type&gt;: &lt;message&gt;"</c>,
    /// e.g. <c>"InvalidOperationException: boom"</c>.
    /// </summary>
    public static string? Describe(Exception? exception) =>
        exception is null ? null : $"{exception.GetType().Name}: {exception.Message}";
}
