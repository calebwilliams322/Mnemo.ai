using System.Linq.Expressions;

namespace Mnemo.Application.Services;

/// <summary>
/// Background job service for queueing async work.
/// Wraps Hangfire to allow for testing and potential future replacement.
/// </summary>
public interface IBackgroundJobService
{
    /// <summary>
    /// Enqueue a job for immediate execution.
    /// </summary>
    /// <returns>Job ID for tracking</returns>
    string Enqueue<T>(Expression<Func<T, Task>> methodCall);

    /// <summary>
    /// Schedule a job for delayed execution.
    /// </summary>
    /// <returns>Job ID for tracking</returns>
    string Schedule<T>(Expression<Func<T, Task>> methodCall, TimeSpan delay);

    /// <summary>
    /// Schedule a job for execution at a specific time.
    /// </summary>
    /// <returns>Job ID for tracking</returns>
    string Schedule<T>(Expression<Func<T, Task>> methodCall, DateTimeOffset enqueueAt);
}
