// Real integration test against the InfiniAnalytics API. Never run in CI.
//
// Requires INFINIANALYTICS_TOKEN and INFINIANALYTICS_AUTOMATION_ID (and optionally
// INFINIANALYTICS_BASE_URL), read from a .env file (see .env.example) or from environment
// variables, which take precedence. The token is never printed.
//
// The SDK never throws on API failures: every call returns null (and logs it) instead.
// This program checks each result explicitly so a real failure is noticed.
//
// Usage: copy .env.example to .env, fill it in and run
//   dotnet run --project samples/SmokeTest

using System;
using System.IO;
using System.Threading.Tasks;
using InfiniAnalytics;

LoadDotEnv();

var token = Environment.GetEnvironmentVariable("INFINIANALYTICS_TOKEN");
var automationId = Environment.GetEnvironmentVariable("INFINIANALYTICS_AUTOMATION_ID");
var baseUrl = Environment.GetEnvironmentVariable("INFINIANALYTICS_BASE_URL");

if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(automationId))
{
    Console.Error.WriteLine(
        "Missing INFINIANALYTICS_TOKEN and/or INFINIANALYTICS_AUTOMATION_ID. Create a .env file from .env.example.");
    return 1;
}

var errors = 0;
var client = new InfiniAnalyticsClient(new InfiniAnalyticsClientOptions
{
    Token = token,
    BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? InfiniAnalyticsClientOptions.DefaultBaseUrl : baseUrl,
    Logger = (level, message, exception) =>
    {
        if (level == InfiniAnalyticsLogLevel.Error)
        {
            errors++;
        }

        Console.Error.WriteLine($"   [{level}] {message} {exception?.Message}");
    },
});

Console.WriteLine("1) PingAsync() [GET /health]...");
Check("ping", await client.PingAsync());

Console.WriteLine("2) StartExecutionAsync() [START]...");
var execution = await client.StartExecutionAsync(automationId, description: "smoke-test: starting");
Check("start", errors == 0);
Console.WriteLine($"   -> executionId: {execution.ExecutionId}");

Console.WriteLine("3) EventAsync() [EVENT]...");
Check("event", await execution.EventAsync("smoke-test: milestone reached") is not null);

Console.WriteLine("4) WarningAsync() [WARNING]...");
Check("warning", await execution.WarningAsync("smoke-test: non-fatal warning") is not null);

Console.WriteLine("5) ErrorAsync() [ERROR]...");
Check("error", await execution.ErrorAsync(
    "smoke-test: simulated failure", new InvalidOperationException("simulated error for smoke test")) is not null);

Console.WriteLine("6) End() [END, synchronous API]...");
var end = await Task.Run(() => execution.End("smoke-test: finished"));
Check("end", end is not null);
Console.WriteLine($"   -> createdAt: {end!.CreatedAt:O}, environment: {end.Environment}");

Console.WriteLine();
Console.WriteLine("Smoke test OK: every event was registered.");
return 0;

static void Check(string step, bool ok)
{
    if (!ok)
    {
        Console.Error.WriteLine($"\nSmoke test FAIL: step '{step}' failed (see the log above).");
        Environment.Exit(1);
    }

    Console.WriteLine("   -> ok");
}

// Minimal .env reader: KEY=VALUE lines, '#' comments, optional quotes. Looks for the file in
// the current directory and its parents. Variables already set in the environment win.
static void LoadDotEnv()
{
    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
    {
        var path = Path.Combine(directory.FullName, ".env");
        if (!File.Exists(path))
        {
            continue;
        }

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        Console.WriteLine($"Loaded settings from {path}");
        return;
    }
}
