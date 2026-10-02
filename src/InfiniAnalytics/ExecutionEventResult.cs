using System;

namespace InfiniAnalytics;

/// <summary>
/// Event as stored by the API, returned for every successfully registered event.
/// Missing or <see langword="null"/> text fields are returned as an empty string.
/// </summary>
public sealed class ExecutionEventResult
{
    internal ExecutionEventResult(
        string automationId,
        string executionId,
        EventType? eventType,
        string description,
        string errorId,
        string errorDescription,
        DateTimeOffset? createdAt,
        int? environment)
    {
        AutomationId = automationId;
        ExecutionId = executionId;
        EventType = eventType;
        Description = description;
        ErrorId = errorId;
        ErrorDescription = errorDescription;
        CreatedAt = createdAt;
        Environment = environment;
    }

    /// <summary>UUID of the automation (<c>automation</c> in the response).</summary>
    public string AutomationId { get; }

    /// <summary>Identifier of the execution (<c>execution_id</c>).</summary>
    public string ExecutionId { get; }

    /// <summary>
    /// Type of the event (<c>type_of_event</c>), or <see langword="null"/> if the
    /// API returned a value this SDK version does not know.
    /// </summary>
    public EventType? EventType { get; }

    /// <summary>Description of the event (<c>description</c>).</summary>
    public string Description { get; }

    /// <summary>Error code (<c>error_id</c>).</summary>
    public string ErrorId { get; }

    /// <summary>Error detail (<c>error_description_detail</c> in the response).</summary>
    public string ErrorDescription { get; }

    /// <summary>When the API stored the event (<c>created_at</c>), if it could be parsed.</summary>
    public DateTimeOffset? CreatedAt { get; }

    /// <summary>Environment the event was assigned to (<c>environment</c>), if present.</summary>
    public int? Environment { get; }
}
