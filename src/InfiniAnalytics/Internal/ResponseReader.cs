using System;
using System.Globalization;
using System.Text.Json;

namespace InfiniAnalytics.Internal;

/// <summary>
/// Maps the <c>201</c> body of <c>POST /v1/register/</c> to an <see cref="ExecutionEventResult"/>.
/// Lenient on purpose: an empty, non-JSON or partial body yields empty values instead of failing.
/// </summary>
internal static class ResponseReader
{
    public static ExecutionEventResult Read(string body)
    {
        var root = Parse(body);

        return new ExecutionEventResult(
            automationId: GetString(root, "automation"),
            executionId: GetString(root, "execution_id"),
            eventType: EventTypeNames.FromWire(GetString(root, "type_of_event")),
            description: GetString(root, "description"),
            errorId: GetString(root, "error_id"),
            errorDescription: GetString(root, "error_description_detail"),
            createdAt: GetDateTimeOffset(root, "created_at"),
            environment: GetInt32(root, "environment"));
    }

    private static JsonElement? Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? GetProperty(JsonElement? root, string name) =>
        root is { } element && element.TryGetProperty(name, out var value) ? value : null;

    private static string GetString(JsonElement? root, string name)
    {
        if (GetProperty(root, name) is not { } value)
        {
            return "";
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => value.GetRawText(),
        };
    }

    private static int? GetInt32(JsonElement? root, string name)
    {
        if (GetProperty(root, name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement? root, string name)
    {
        var text = GetString(root, name);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;
    }
}
