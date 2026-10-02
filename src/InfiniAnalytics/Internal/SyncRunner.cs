using System;
using System.Threading.Tasks;

namespace InfiniAnalytics.Internal;

/// <summary>
/// Runs an async operation synchronously for hosts where awaiting is not practical
/// (UiPath Invoke Code, Blue Prism code stages). The operation runs on the thread pool,
/// so it never needs the caller's synchronization context and cannot deadlock on it.
/// </summary>
internal static class SyncRunner
{
    public static T Run<T>(Func<Task<T>> operation) => Task.Run(operation).GetAwaiter().GetResult();
}
