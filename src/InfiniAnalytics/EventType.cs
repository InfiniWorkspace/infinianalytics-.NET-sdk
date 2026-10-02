namespace InfiniAnalytics;

/// <summary>
/// Type of an event registered for an execution. Sent to the API as
/// <c>START</c>, <c>EVENT</c>, <c>WARNING</c>, <c>ERROR</c> or <c>END</c>.
/// </summary>
public enum EventType
{
    /// <summary>Opens an execution.</summary>
    Start,

    /// <summary>Relevant milestone during the execution.</summary>
    Event,

    /// <summary>Non-critical issue during the execution.</summary>
    Warning,

    /// <summary>
    /// Closes the execution as failed. Later events with the same execution id
    /// (within one hour) are attached to that same execution, and a later
    /// <see cref="End"/> leaves it as finished with errors.
    /// </summary>
    Error,

    /// <summary>Closes the execution as finished.</summary>
    End,
}
