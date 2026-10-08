# InfiniAnalytics for .NET

**English** | [Español](https://github.com/InfiniWorkspace/infinianalytics-.NET-sdk/blob/main/README.es.md)

InfiniAnalytics is a .NET library that makes it easy to register events, process starts and
ends, and errors on the Infini Analytics platform.

It is designed for automations written in .NET: C# applications and services, Power Automate
Desktop flows ("Run .NET script" action), UiPath robots ("Invoke Code" activity) and Blue Prism
code stages.

It is designed to never break the flow of your automation or application: on any communication
error, timeout or unexpected API response, the library writes the failure to the log and returns
`null` (or `false` in the case of `Ping`), but it does not throw exceptions.

## Main features

- Register the start (`client.StartExecutionAsync(automationId, executionId?, description?)`)
- Register an event (`execution.EventAsync(description)`)
- Register a warning (`execution.WarningAsync(description)`)
- Register errors (`execution.ErrorAsync(description, exception?, errorId?, errorDescription?)`)
- Register the end (`execution.EndAsync(description)`)
- A synchronous version of each method (`StartExecution`, `Event`, `Warning`, `Error`, `End`) for
  environments where using `await` is not convenient, with no risk of deadlocks.
- **Does not throw exceptions** on a communication failure, a timeout or a non-2xx API response:
  each call returns `null` and writes the failure to the log. Incorrect use of the SDK itself,
  such as not passing a token, does throw an exception when creating the client: that is a
  programming error, not a communication failure.
- Short default timeout (10 seconds), so a robot does not hang if the API does not respond.
- The token is never written to the log.

## Compatibility

| Platform | Build used |
|---|---|
| .NET Framework 4.6.2 or later (Power Automate Desktop, UiPath Legacy, Blue Prism) | `net462` |
| .NET 10 or later | `net10.0` (no external dependencies) |
| Other platforms compatible with .NET Standard 2.0 (.NET 6, 8, 9, Mono...) | `netstandard2.0` |

## Installation

```bash
dotnet add package InfiniAnalytics.Sdk
```

In UiPath, install it from "Manage Packages" by searching for `InfiniAnalytics.Sdk`. Power
Automate Desktop and Blue Prism do not install NuGet packages: download the
`InfiniAnalytics-<version>-dotnet-framework-dlls.zip` file from the
[Releases](https://github.com/InfiniWorkspace/infinianalytics-.NET-sdk/releases) page, which
contains the SDK and all its dependencies, and see the
[Power Automate Desktop](#power-automate-desktop) and [Blue Prism](#blue-prism) sections.

## Basic usage

### 1. Create the client and start an execution

```csharp
using InfiniAnalytics;

var client = new InfiniAnalyticsClient("YOUR_TOKEN");

var execution = await client.StartExecutionAsync(
    "44444444-4444-4444-4444-444444444444",
    description: "Starting the process");
```

- `token`: your organization token, which identifies you for authentication. You will find it in
  the InfiniAnalytics dashboard. Do not write it in the code: read it from the configuration, an
  environment variable or a credential store (Orchestrator Assets in UiPath, Credentials in Blue
  Prism).
- `automationId`: identifier (UUID) of your automation.
- `executionId` (optional): identifier of the execution. If you omit it, one is generated
  automatically.

`StartExecutionAsync(...)` registers the `START` event and returns an `Execution` with which to
register the rest of the events of that execution. It always returns an `Execution`, even if
registering the `START` fails, so that your automation can keep calling its methods.

The client is safe to use from multiple threads: create one and reuse it.

### 2. Register events

#### Intermediate event (`EventAsync`)

```csharp
await execution.EventAsync("An event occurred");
```

Registers a relevant event (a milestone) during the execution.

#### Warning (`WarningAsync`)

```csharp
await execution.WarningAsync("Warning: This is a non-critical issue");
```

Registers a warning during the execution. Warnings are useful to report situations that are not
critical errors but need attention, such as unexpected values, suboptimal configurations or
conditions that could lead to future problems.

#### Error (`ErrorAsync`)

To register an error inside a `try/catch`:

```csharp
try
{
    // Logic that may fail
}
catch (Exception ex)
{
    await execution.ErrorAsync("An error occurred during the process", ex, errorId: "1234");
}
```

If you pass the exception, the error detail (`error_description`) is derived from it with the
format `"<Type>: <message>"`, for example `InvalidOperationException: boom`. You can also pass
`errorDescription` explicitly, which takes precedence over the one derived from the exception:

```csharp
await execution.ErrorAsync(
    "An error occurred during the process",
    errorId: "1234",
    errorDescription: ex.ToString());
```

**Important:** always call `execution.ErrorAsync(...)` inside a `catch` block, to make sure you
capture any exception and report it to the API. The library does not throw exceptions, so your
automation will not stop.

#### End (`EndAsync`)

```csharp
await execution.EndAsync("Ending the process");
```

Registers the end of the execution.

#### Result of each call

Each method returns the event as stored by the API (`ExecutionEventResult`, with
`AutomationId`, `ExecutionId`, `EventType`, `Description`, `ErrorId`, `ErrorDescription`,
`CreatedAt` and `Environment`), or `null` if it could not be registered. You do not need to check
it: the failure is already written to the log.

## Event semantics

| Event | Method | Effect on the execution |
|---|---|---|
| `START` | `StartExecutionAsync` | Opens an execution. |
| `EVENT` | `EventAsync` | Intermediate milestone. |
| `WARNING` | `WarningAsync` | Intermediate milestone that needs attention. |
| `ERROR` | `ErrorAsync` | Closes the execution as failed (see below). |
| `END` | `EndAsync` | Closes the execution as finished. |

An `ERROR` does not force you to open another execution. If more events with the same
`executionId` arrive less than 1 hour after an `ERROR`, they are associated with that same
execution again, and a later `END` leaves it in the "finished with error" state. This way you can
report non-fatal errors and continue with the process:

```csharp
foreach (var invoice in invoices)
{
    try
    {
        Process(invoice);
    }
    catch (Exception ex)
    {
        await execution.ErrorAsync($"Invoice {invoice.Number} not processed", ex);
    }
}

await execution.EndAsync("Process finished");
```

### Execution identifier (`executionId`)

Each time you call `StartExecutionAsync(...)`, it uses the `executionId` you pass or, if you do
not pass one (or pass an empty string), it generates one from the current UTC date and time in
ISO 8601 format, for example `2026-10-02T15:04:05.123Z`.

The identifier is generated by the SDK on the machine where your automation runs, not by the
API. This is how the value travels:

1. `StartExecutionAsync(...)` uses the `executionId` you pass or, if you do not pass one,
   generates a new one from the current time.
2. It stores it in the `ExecutionId` property of the returned `Execution` and sends it to the API
   with the `START` event.
3. `EventAsync`, `WarningAsync`, `ErrorAsync` and `EndAsync` send that same identifier. This is
   how the API groups all the events into a single execution.
4. If you need to keep registering events in another step that cannot receive the `Execution`
   object (another UiPath activity, another Power Automate Desktop action, another Blue Prism
   stage), read `execution.ExecutionId`, pass it to that step and rebuild the `Execution` with
   `new Execution(client, automationId, executionId)`. This does not send a new `START`.

```
StartExecution(automationId)            the SDK generates "2026-10-02T15:04:05.123Z"
  -> sends START with that id to the API
  -> returns execution (execution.ExecutionId = "2026-10-02T15:04:05.123Z")
execution.Event / Warning / Error / End -> send the same id
execution.ExecutionId                   -> you can store it and pass it to other steps
```

The API does not return or change the identifier: what you see in the dashboard is exactly the
value the SDK generated or received.

An `executionId` identifies one execution and must not be reused in two simultaneous executions
of the same automation. If an automation can run in parallel (for example, on several robots at
once), pass your own unique identifier, such as `Guid.NewGuid().ToString()`.

## Synchronous usage

Each asynchronous method has a synchronous version with the same name without the `Async`
suffix: `StartExecution`, `Event`, `Warning`, `Error`, `End`, `Register` and `Ping`. They can be
called from any thread (including UI threads and the robot thread) with no risk of deadlocks.

```csharp
var client = new InfiniAnalyticsClient("YOUR_TOKEN");
var execution = client.StartExecution("44444444-4444-4444-4444-444444444444", description: "Start");

execution.Event("Invoices downloaded");
execution.End("End of the process");
```

### Power Automate Desktop

It is used from the "Run .NET script" action in C#, loading the SDK from the DLL folder. See the
step-by-step guide in
[docs/power-automate-desktop.md](https://github.com/InfiniWorkspace/infinianalytics-.NET-sdk/blob/main/docs/power-automate-desktop.md).

### UiPath (Invoke Code)

1. Install the `InfiniAnalytics.Sdk` package from "Manage Packages".
2. In the "Imports" panel of the workflow, add the `InfiniAnalytics` namespace.
3. In the "Invoke Code" activity (VB.NET by default):

```vb
Dim client As New InfiniAnalyticsClient(token)
Dim execution = client.StartExecution(automationId, description:="Start of the process")
Try
    execution.Event("Invoices downloaded")
Catch ex As Exception
    execution.Error("Failed to process invoices", ex, errorId:="E001")
End Try
execution.End("End of the process")
executionId = execution.ExecutionId
```

Where `token` and `automationId` are input arguments and `executionId` is an output argument of
the activity. Read the token from an Orchestrator Asset instead of writing it in the workflow.

Objects do not survive from one "Invoke Code" to another. To register more events of the same
execution in another activity, pass it the `executionId` as an argument and rebuild the
`Execution` (this does not send a new `START`):

```vb
Dim execution As New Execution(New InfiniAnalyticsClient(token), automationId, executionId)
execution.End("End of the process")
```

### Blue Prism

1. Copy the contents of the `InfiniAnalytics-<version>-dotnet-framework-dlls.zip` file into the
   Blue Prism installation folder: the `InfiniAnalytics.dll` DLL and its dependencies,
   `System.Text.Json.dll`, `System.Text.Encodings.Web.dll`, `System.Memory.dll`,
   `System.Buffers.dll`, `System.Numerics.Vectors.dll`, `System.Runtime.CompilerServices.Unsafe.dll`,
   `System.Threading.Tasks.Extensions.dll`, `System.ValueTuple.dll` and
   `Microsoft.Bcl.AsyncInterfaces.dll`.
2. In the "Code Options" of the business object, add `InfiniAnalytics.dll` to the external
   references and `InfiniAnalytics` to the namespace imports.
3. Use the synchronous API in the code stages, as in the UiPath example. Store the `executionId`
   in a data item to rebuild the `Execution` in other stages.

## Checking connectivity

```csharp
bool reachable = await client.PingAsync();
```

Returns `true` if the API responds with a 2xx status code and `false` otherwise.

**Note:** for now `PingAsync` calls `GET /health`, which confirms that the API is reachable but
**does not validate the token**. When the backend publishes `GET /v1/ping/`, the SDK will switch
to it and will also validate the token. To check that the token and the `automationId` are
correct, register a test execution.

## Low-level API

If you need full control over the payload (or want to send your own `automationId` and
`executionId` on each call instead of using the `Execution` returned by `StartExecutionAsync`),
use `client.RegisterAsync()` directly:

```csharp
await client.RegisterAsync(new RegisterEventPayload
{
    AutomationId = "44444444-4444-4444-4444-444444444444",
    ExecutionId = "2025-03-03T15:32:17.571404",
    Event = EventType.Start,
    Description = "Starting the execution",
});
```

## Configuration

```csharp
var client = new InfiniAnalyticsClient(new InfiniAnalyticsClientOptions
{
    Token = configuration["InfiniAnalytics:Token"],
    BaseUrl = "https://api.analytics.infini.es",
    Timeout = TimeSpan.FromSeconds(10),
    Logger = (level, message, exception) => Console.WriteLine($"{level}: {message} {exception?.Message}"),
});
```

| Option | Default | Description |
|---|---|---|
| `Token` | (required) | Organization token. Leading and trailing spaces and line breaks are removed. |
| `BaseUrl` | `https://api.analytics.infini.es` | Base URL of the API. |
| `Timeout` | 10 seconds | Maximum time for each call. When it runs out, the call is written to the log and returns `null`. `Timeout.InfiniteTimeSpan` disables it. |
| `Logger` | Standard error output (`Console.Error`) | Receives the SDK messages (`Warning` or `Error` level, message and exception if any). |

### Logging

By default, failures are written to the standard error output with the `[InfiniAnalytics]`
prefix. In a robot that output is usually not visible, so it is a good idea to redirect it with
`Logger`. It never contains the token. If the logger itself throws an exception, it is ignored.

To use `Microsoft.Extensions.Logging`:

```csharp
ILogger logger = loggerFactory.CreateLogger("InfiniAnalytics");

var options = new InfiniAnalyticsClientOptions
{
    Token = "YOUR_TOKEN",
    Logger = (level, message, exception) => logger.Log(
        level == InfiniAnalyticsLogLevel.Error ? LogLevel.Error : LogLevel.Warning,
        exception,
        "{Message}",
        message),
};
```

### HttpClient and IHttpClientFactory

Unless you pass another one, the SDK uses an internal `HttpClient` shared by the whole process,
so creating many clients (for example, one in each "Invoke Code") does not exhaust sockets and
there is nothing to dispose.

You can also pass your own `HttpClient`, for example one managed by `IHttpClientFactory`. The SDK
does not dispose it and does not use its `BaseAddress` (the URL is taken from `BaseUrl`):

```csharp
builder.Services.AddHttpClient<InfiniAnalyticsClient>((http, services) =>
    new InfiniAnalyticsClient(http, new InfiniAnalyticsClientOptions
    {
        Token = builder.Configuration["InfiniAnalytics:Token"]!,
    }));
```

### Length limits

The API rejects (with a 422 error) texts that exceed these limits. To avoid losing the event, the
SDK truncates them before sending and writes a warning to the log:

| Field | Maximum |
|---|---|
| `description` | 2000 characters |
| `error_id` | 255 characters |
| `error_description` | 5000 characters |

### Timeout and cancellation

All asynchronous methods accept an optional `CancellationToken`:

- If the SDK `Timeout` runs out, the call is written to the log and returns `null`, like any
  other communication failure.
- If you cancel the `CancellationToken` yourself, the method throws `OperationCanceledException`,
  as usual in .NET: cancellation is your decision, not a communication failure.

## Troubleshooting

- **I do not see the SDK errors in the robot log.** By default they are written to
  `Console.Error`. Configure `Logger` to send them to the log of your platform.
- **TLS or secure connection error on old .NET Framework.** The API requires TLS 1.2.
  Applications compiled for .NET Framework 4.7 or later use it by default; on earlier versions
  you may need to enable it in the application that hosts the robot. The SDK does not change this
  setting because it affects the whole process.
- **`FileNotFoundException` or `FileLoadException` for `System.Text.Json` or another DLL on .NET
  Framework.** Dependencies are missing next to `InfiniAnalytics.dll`, or the application that
  hosts the robot loads another version. Copy all the dependencies listed in the
  [Blue Prism](#blue-prism) section and, if needed, add assembly binding redirects
  (`bindingRedirect`) to the `.config` file of the application.
- **The API responds 401.** The token is not valid, has expired or has no access to that
  automation.
- **The API responds 400.** The automation does not exist or the event type is not valid.
- **The API responds 422.** The body is not valid, for example because `automationId` is not a
  UUID.

## Development

Requires the .NET 10 SDK. The .NET Framework 4.8 tests only run on Windows.

```bash
dotnet build
dotnet test
dotnet pack src/InfiniAnalytics -c Release -o artifacts
```

To build the DLL folder for Power Automate Desktop and Blue Prism (the SDK with all its
dependencies):

```bash
dotnet build tools/DllBundle -c Release -o artifacts/dll-bundle
```

`DllBundle.dll` is only the helper project and is not part of the distributed folder.

No test calls the real API. For a real integration test (it does not run in CI), copy
`.env.example` to `.env` in the repository root, fill in the organization token and the
`automationId` of a test automation, and run:

```bash
dotnet run --project samples/SmokeTest
```

`.env` is in `.gitignore` and is never pushed to the repository. Environment variables with the
same name, if they exist, take precedence over the file.

## License

MIT. See the [LICENSE](https://github.com/InfiniWorkspace/infinianalytics-.NET-sdk/blob/main/LICENSE) file.
