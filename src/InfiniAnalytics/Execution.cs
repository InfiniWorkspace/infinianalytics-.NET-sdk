using System;
using System.Threading;
using System.Threading.Tasks;
using InfiniAnalytics.Internal;

namespace InfiniAnalytics;

/// <summary>
/// Handle for a single execution of an automation, returned by
/// <see cref="InfiniAnalyticsClient.StartExecutionAsync"/>. Remembers the automation and
/// execution ids so the following events do not need to repeat them.
/// </summary>
/// <remarks>
/// Like the client, its methods never throw for communication failures: they return
/// <see langword="null"/> after logging the problem.
/// </remarks>
public sealed class Execution
{
    private readonly InfiniAnalyticsClient _client;

    /// <summary>
    /// Creates a handle for an execution that was already started, without sending a START
    /// event. Useful when the ids are passed between steps that cannot share objects
    /// (e.g. separate UiPath Invoke Code activities or Blue Prism stages).
    /// </summary>
    /// <param name="client">Client used to send the events.</param>
    /// <param name="automationId">UUID of the automation in the InfiniAnalytics platform.</param>
    /// <param name="executionId">Identifier used when the execution was started.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    public Execution(InfiniAnalyticsClient client, string automationId, string executionId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        AutomationId = automationId ?? "";
        ExecutionId = executionId ?? "";
    }

    /// <summary>UUID of the automation in the InfiniAnalytics platform.</summary>
    public string AutomationId { get; }

    /// <summary>Identifier of this execution.</summary>
    public string ExecutionId { get; }

    /// <summary>Registers a relevant event or milestone during the execution.</summary>
    /// <param name="description">Description of the event.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The event as stored by the API, or <see langword="null"/> if it could not be registered.</returns>
    public Task<ExecutionEventResult?> EventAsync(string? description = null, CancellationToken cancellationToken = default) =>
        RegisterAsync(EventType.Event, description, null, null, cancellationToken);

    /// <summary>Registers a non-critical warning during the execution.</summary>
    /// <param name="description">Description of the warning.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The event as stored by the API, or <see langword="null"/> if it could not be registered.</returns>
    public Task<ExecutionEventResult?> WarningAsync(string? description = null, CancellationToken cancellationToken = default) =>
        RegisterAsync(EventType.Warning, description, null, null, cancellationToken);

    /// <summary>
    /// Registers an error. The execution is closed as failed, but later events with the same
    /// execution id (within one hour) are still attached to it, and a later
    /// <see cref="EndAsync"/> leaves it as finished with errors.
    /// </summary>
    /// <param name="description">High-level description of the error.</param>
    /// <param name="exception">
    /// The caught exception. When <paramref name="errorDescription"/> is not given, it is derived
    /// from it as <c>"&lt;Type&gt;: &lt;message&gt;"</c>.
    /// </param>
    /// <param name="errorId">Error code.</param>
    /// <param name="errorDescription">Error detail; takes precedence over the one derived from <paramref name="exception"/>.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The event as stored by the API, or <see langword="null"/> if it could not be registered.</returns>
    public Task<ExecutionEventResult?> ErrorAsync(
        string? description = null,
        Exception? exception = null,
        string? errorId = null,
        string? errorDescription = null,
        CancellationToken cancellationToken = default) =>
        RegisterAsync(
            EventType.Error,
            description,
            errorId,
            errorDescription ?? ErrorDescriber.Describe(exception),
            cancellationToken);

    /// <summary>Registers the end of the execution.</summary>
    /// <param name="description">Description of the end event.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The event as stored by the API, or <see langword="null"/> if it could not be registered.</returns>
    public Task<ExecutionEventResult?> EndAsync(string? description = null, CancellationToken cancellationToken = default) =>
        RegisterAsync(EventType.End, description, null, null, cancellationToken);

    /// <summary>Synchronous version of <see cref="EventAsync"/>, safe to call from any thread.</summary>
    /// <param name="description">Description of the event.</param>
    /// <returns>The event as stored by the API, or <see langword="null"/> if it could not be registered.</returns>
    public ExecutionEventResult? Event(string? description = null) => SyncRunner.Run(() => EventAsync(description));

    /// <summary>Synchronous version of <see cref="WarningAsync"/>, safe to call from any thread.</summary>
    /// <param name="description">Description of the warning.</param>
    /// <returns>The event as stored by the API, or <see langword="null"/> if it could not be registered.</returns>
    public ExecutionEventResult? Warning(string? description = null) => SyncRunner.Run(() => WarningAsync(description));

    /// <summary>Synchronous version of <see cref="ErrorAsync"/>, safe to call from any thread.</summary>
    /// <param name="description">High-level description of the error.</param>
    /// <param name="exception">The caught exception, used to derive the error detail when it is not given.</param>
    /// <param name="errorId">Error code.</param>
    /// <param name="errorDescription">Error detail; takes precedence over the one derived from <paramref name="exception"/>.</param>
    /// <returns>The event as stored by the API, or <see langword="null"/> if it could not be registered.</returns>
    public ExecutionEventResult? Error(
        string? description = null, Exception? exception = null, string? errorId = null, string? errorDescription = null) =>
        SyncRunner.Run(() => ErrorAsync(description, exception, errorId, errorDescription));

    /// <summary>Synchronous version of <see cref="EndAsync"/>, safe to call from any thread.</summary>
    /// <param name="description">Description of the end event.</param>
    /// <returns>The event as stored by the API, or <see langword="null"/> if it could not be registered.</returns>
    public ExecutionEventResult? End(string? description = null) => SyncRunner.Run(() => EndAsync(description));

    private Task<ExecutionEventResult?> RegisterAsync(
        EventType eventType,
        string? description,
        string? errorId,
        string? errorDescription,
        CancellationToken cancellationToken) =>
        _client.RegisterAsync(
            new RegisterEventPayload
            {
                AutomationId = AutomationId,
                ExecutionId = ExecutionId,
                Event = eventType,
                Description = description,
                ErrorId = errorId,
                ErrorDescription = errorDescription,
            },
            cancellationToken);
}
