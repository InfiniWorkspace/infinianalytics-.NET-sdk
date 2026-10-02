using System;
using System.IO;
using System.Text.Json;

namespace InfiniAnalytics.Internal;

/// <summary>Writes the snake_case JSON body of <c>POST /v1/register/</c>.</summary>
internal static class PayloadWriter
{
    /// <param name="payload">Payload to serialize.</param>
    /// <param name="eventName">Wire name of <see cref="RegisterEventPayload.Event"/>.</param>
    /// <param name="onTruncated">Called with the field name, its limit and its original length.</param>
    public static byte[] Write(RegisterEventPayload payload, string eventName, Action<string, int, int> onTruncated)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("automation_id", payload.AutomationId ?? "");
            writer.WriteString("execution_id", payload.ExecutionId ?? "");
            writer.WriteString("event", eventName);
            WriteOptional(writer, "description", payload.Description, TextLimits.Description, onTruncated);
            WriteOptional(writer, "error_id", payload.ErrorId, TextLimits.ErrorId, onTruncated);
            WriteOptional(writer, "error_description", payload.ErrorDescription, TextLimits.ErrorDescription, onTruncated);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteOptional(
        Utf8JsonWriter writer, string name, string? value, int max, Action<string, int, int> onTruncated)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > max)
        {
            onTruncated(name, max, value.Length);
            value = TextLimits.Truncate(value, max);
        }

        writer.WriteString(name, value);
    }
}
