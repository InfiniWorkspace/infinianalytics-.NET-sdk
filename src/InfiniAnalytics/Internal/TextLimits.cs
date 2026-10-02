namespace InfiniAnalytics.Internal;

/// <summary>Maximum lengths accepted by the API; longer values are rejected with a 422.</summary>
internal static class TextLimits
{
    public const int Description = 2000;
    public const int ErrorId = 255;
    public const int ErrorDescription = 5000;

    /// <summary>
    /// Cuts <paramref name="value"/> to at most <paramref name="max"/> UTF-16 code units
    /// without splitting a surrogate pair. The API counts code points, which are never
    /// more than UTF-16 code units, so the result always fits.
    /// </summary>
    public static string Truncate(string value, int max)
    {
        if (value.Length <= max)
        {
            return value;
        }

        var length = char.IsHighSurrogate(value[max - 1]) ? max - 1 : max;
        return value.Substring(0, length);
    }
}
