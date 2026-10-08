# InfiniAnalytics in Power Automate Desktop

**English** | [Español](power-automate-desktop.es.md)

This guide explains how to register the executions of a Power Automate Desktop (PAD) flow in
InfiniAnalytics using the .NET SDK from the "Run .NET script" action.

It is only for desktop flows. Power Automate cloud flows cannot run .NET code.

## Requirements

- Power Automate Desktop with the "Run .NET script" action ("Scripting" group).
- Your organization token, which you will find in the InfiniAnalytics dashboard.
- The identifier (UUID) of the automation in InfiniAnalytics.
- The `InfiniAnalytics-<version>-dotnet-framework-dlls.zip` file, downloaded from the
  [Releases](https://github.com/InfiniWorkspace/infinianalytics-.NET-sdk/releases) page of the
  repository.

## What the flow looks like

At the end of this guide, the flow will look like this:

```
Main
  Set variable              IA_Token, IA_AutomationId, IA_EventType, IA_Description,
                            IA_ErrorId, IA_ErrorDetail             (section 3)
  Run .NET script           start: registers START                 (section 4)

  ... actions of your process ...
  Set variable              IA_EventType = EVENT, IA_Description = ...  (section 6)
  Run subflow               IA_Register

  On block error                                                   (section 7)
    ... actions of your process that may fail ...
  End

  Set variable              IA_EventType = END, IA_Description = ...    (section 6)
  Run subflow               IA_Register

Subflow IA_Register         registers EVENT, WARNING, ERROR or END (section 5)
Subflow IA_Error            registers the error of the block       (section 7)
```

There are only two pieces of C# code, which you copy as they are: the start code (in Main) and
the register code (in the `IA_Register` subflow). The rest of the flow is regular PAD actions.

## 1. Prepare the DLL folder

1. Download the `.zip` and, before extracting it, right-click it, open "Properties" and check
   "Unblock" if it appears. This way Windows does not block the DLLs for coming from the
   Internet.
2. Extract it to a fixed folder on the machine where the flow runs, for example
   `C:\InfiniAnalytics\dlls`. It must contain these 10 DLLs:
   - `InfiniAnalytics.dll`
   - `Microsoft.Bcl.AsyncInterfaces.dll`
   - `System.Buffers.dll`
   - `System.Memory.dll`
   - `System.Numerics.Vectors.dll`
   - `System.Runtime.CompilerServices.Unsafe.dll`
   - `System.Text.Encodings.Web.dll`
   - `System.Text.Json.dll`
   - `System.Threading.Tasks.Extensions.dll`
   - `System.ValueTuple.dll`

If the flow runs on several machines, the folder must exist on all of them with the same path.

## 2. Three ideas before you start

**Script parameters connect the C# code with the flow.** In the "Script parameters" table of
the "Run .NET script" action, each row is a value that goes into or comes out of the code:

| Column | What you write |
|---|---|
| Parameter name | The name used by the code. Copy it exactly from this guide, do not make it up. |
| Direction | `In` if the value goes into the code, `Out` if the code returns it. |
| Input value | Only in `In` rows: where the value comes from. A flow variable with `%` (for example `%IA_Token%`) or a fixed text. |
| Output variable | Only in `Out` rows: the name of the variable where the result is stored, without `%`. |

**Input variables are created beforehand; output variables are not.** A variable used as an
input value must exist when the action runs, so you create it earlier with "Set variable". A
variable used as an output variable is created by the action itself when it finishes, just like
"Display message" creates `ButtonPressed`.

**Variables are shared by the whole flow.** A variable created in Main can be used in any
subflow. To change its value, add another "Set variable" action with the same name: it does not
create a new variable, it replaces the value.

## 3. Create the variables

At the beginning of Main, add one "Set variable" action for each row:

| Variable | Value |
|---|---|
| `IA_Token` | Your token |
| `IA_AutomationId` | The identifier of the automation |
| `IA_EventType` | `%''%` |
| `IA_Description` | `%''%` |
| `IA_ErrorId` | `%''%` |
| `IA_ErrorDetail` | `%''%` |

`%''%` is an empty text: those variables get a value later. In the variables pane, mark
`IA_Token` as sensitive so that it does not appear in the flow logs.

## 4. Register the start (Main)

Below the variables, add the "Run .NET script" action:

| Field | Value |
|---|---|
| Language | `C#` |
| .NET script imports | `System` and `InfiniAnalytics`, **one per line** (see below) |
| References to be loaded | The DLL folder, for example `C:\InfiniAnalytics\dlls` |

The ".NET script imports" field is a text box. Type exactly this:

```
System
InfiniAnalytics
```

**Script parameters** (click "Edit"), exactly these 4 rows, all of type `String`:

| Parameter name | Direction | Input value | Output variable |
|---|---|---|---|
| `token` | In | `%IA_Token%` | |
| `automationId` | In | `%IA_AutomationId%` | |
| `description` | In | `Start of the process` | |
| `executionId` | Out | | `IA_ExecutionId` |

**.NET code to run:**

```csharp
var client = new InfiniAnalyticsClient(token);
var execution = client.StartExecution(automationId, null, description);
executionId = execution.ExecutionId;
```

This action registers the `START` and stores the execution identifier in `%IA_ExecutionId%`.
The subflow in the next section uses it so that all the events appear together in the same
execution in the dashboard.

## 5. Subflow to register events (`IA_Register`)

Create a subflow named `IA_Register` with a single "Run .NET script" action. Language, imports
and references, the same as in section 4.

**Script parameters**, exactly these 8 rows, all of type `String`:

| Parameter name | Direction | Input value | Output variable |
|---|---|---|---|
| `token` | In | `%IA_Token%` | |
| `automationId` | In | `%IA_AutomationId%` | |
| `executionId` | In | `%IA_ExecutionId%` | |
| `eventType` | In | `%IA_EventType%` | |
| `description` | In | `%IA_Description%` | |
| `errorId` | In | `%IA_ErrorId%` | |
| `errorDetail` | In | `%IA_ErrorDetail%` | |
| `result` | Out | | `IA_Result` |

**.NET code to run:**

```csharp
var execution = new Execution(new InfiniAnalyticsClient(token), automationId, executionId);
object registered = null;
switch (eventType)
{
    case "EVENT":
        registered = execution.Event(description);
        break;
    case "WARNING":
        registered = execution.Warning(description);
        break;
    case "ERROR":
        registered = execution.Error(
            description,
            null,
            string.IsNullOrEmpty(errorId) ? null : errorId,
            string.IsNullOrEmpty(errorDetail) ? null : errorDetail);
        break;
    case "END":
        registered = execution.End(description);
        break;
}
result = registered != null ? "OK" : "NOT REGISTERED";
```

The code registers whatever `%IA_EventType%` says:

| Value of `IA_EventType` | What it registers |
|---|---|
| `EVENT` | A milestone of the process |
| `WARNING` | A warning that needs attention |
| `ERROR` | An error |
| `END` | The end of the execution |

When it finishes, `%IA_Result%` is `OK` if the API stored the event, or `NOT REGISTERED` if it
could not be registered (network failure, timeout, invalid token or a misspelled
`IA_EventType`). You do not need to check it: the SDK never stops the flow because of a
communication failure.

## 6. Register events and the end (Main)

To register something at any point of Main, add these three actions:

1. "Set variable" `IA_EventType` with the type, for example `EVENT`. Type it as is, without
   quotes or `%`.
2. "Set variable" `IA_Description` with the text, for example `Invoices downloaded`.
3. "Run subflow" `IA_Register`.

At the end of the flow, do the same with `IA_EventType` = `END` and a description such as
`End of the process`. Always register the `END`: without it, the execution stays open in the
dashboard.

## 7. Register the flow errors

If a PAD action fails, the flow stops. To register the error in InfiniAnalytics and still end
the execution properly:

**1. Create the `IA_Error` subflow** with these actions:

| # | Action | Configuration |
|---|---|---|
| 1 | Get last error | Stores the error in `LastError` |
| 2 | Set variable | `IA_EventType` = `ERROR` |
| 3 | Set variable | `IA_Description` = `Process failed` |
| 4 | Set variable | `IA_ErrorDetail` = `%LastError.Message%` |
| 5 | Run subflow | `IA_Register` |

**2. In Main, put the actions of the process in an error block.** Add the "On block error"
action and place the actions that may fail inside it, between the action and its "End". The
`END` actions go **after** the "End" of the block, never inside it.

**3. Configure the block.** Open the "On block error" action:

- Add a new rule ("New rule") to **run the subflow** `IA_Error`.
- Enable **"Continue flow run"** and choose to continue from the **end of the block**.

If you do not enable "Continue flow run", the flow stops after `IA_Error`, the `END` is not
registered and the execution appears in the dashboard with the error status instead of
finished with error.

## 8. Check the result

Run the flow and open the execution in the InfiniAnalytics dashboard. Its execution ID is the
value of `%IA_ExecutionId%`. Depending on the registered events, the status will be:

| Events | Status |
|---|---|
| `START` (and intermediate events), no `END` | Started |
| `START` ... `END` | Finished |
| `START` ... `ERROR` ... `END` | Finished with error |
| `START` ... `ERROR`, no `END` | Error |

## If the flow runs in parallel

The SDK generates the execution identifier from the current UTC date and time, for example
`2026-10-08T13:15:03.649Z`. If the same flow can run at the same time on several machines, two
executions that start in the same millisecond would have the same identifier. To avoid it, pass
a unique identifier instead of `null` in the start code:

```csharp
var execution = client.StartExecution(automationId, Guid.NewGuid().ToString(), description);
```

## Troubleshooting

- **Error saying that variable 'X' has been defined but not initialized.** The flow uses the
  variable `X` before it exists. If it is an input variable, create it earlier with "Set
  variable" (section 3). If it is `IA_ExecutionId`, check that the start action comes earlier
  and that its output variable has exactly that name.
- **"The name 'X' does not exist in the current context"** (compilation error). The code uses a
  parameter `X` that is not in the parameters table, or is spelled differently. Compare the
  table with the one in this guide, including case. It also appears if `InfiniAnalytics` is
  missing from the imports (remember: one namespace per line).
- **Error saying that the value is not unique** in the parameters table. Two rows have the same
  parameter name. Delete one.
- **I cannot type in the input value or output variable column.** The input value is only
  enabled with the `In` direction, and the output variable with the `Out` direction.
- **The action fails because of an output parameter.** Every `Out` parameter must get a value
  in the code. If the table has an `Out` row that the code does not use, delete it.
- **`FileNotFoundException` or `FileLoadException` for some DLL.** One of the 10 DLLs is missing
  from the folder, or the "References to be loaded" path is not correct.
- **Error such as "An attempt was made to load an assembly from a network location".** Windows
  has blocked the DLLs for coming from the Internet. Unblock the `.zip` (section 1) and extract
  it again.
- **The execution has the error status and no `END`.** The error block is not configured to
  continue, or the `END` actions are inside the block (section 7).
- **`%IA_Result%` is `NOT REGISTERED`.** Check the token, the automation identifier, that
  `%IA_EventType%` is one of the four valid values and that the machine has Internet access.
  Each call waits at most 10 seconds for the API.
