namespace InfiniAnalytics;

/// <summary>
/// Payload sent to <c>POST /v1/register/</c> by
/// <see cref="InfiniAnalyticsClient.RegisterAsync(RegisterEventPayload, System.Threading.CancellationToken)"/>.
/// Serialized in snake_case; <see langword="null"/> properties are omitted.
/// </summary>
public sealed class RegisterEventPayload
{
    /// <summary>UUID of the automation in the InfiniAnalytics platform (<c>automation_id</c>).</summary>
    public string AutomationId { get; set; } = "";

    /// <summary>
    /// Identifier of the execution (<c>execution_id</c>). Must not be reused by two
    /// concurrent executions of the same automation.
    /// </summary>
    public string ExecutionId { get; set; } = "";

    /// <summary>Type of the event (<c>event</c>).</summary>
    public EventType Event { get; set; }

    /// <summary>Free text (<c>description</c>). Truncated to 2000 characters.</summary>
    public string? Description { get; set; }

    /// <summary>Error code (<c>error_id</c>). Truncated to 255 characters.</summary>
    public string? ErrorId { get; set; }

    /// <summary>Error detail (<c>error_description</c>). Truncated to 5000 characters.</summary>
    public string? ErrorDescription { get; set; }
}
