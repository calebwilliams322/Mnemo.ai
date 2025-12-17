using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Mnemo.Extraction.Resilience;

/// <summary>
/// Factory for creating resilience pipelines for external service calls.
/// Combines retry with exponential backoff and circuit breaker patterns.
/// </summary>
public static class ResiliencePolicies
{
    /// <summary>
    /// Creates a resilience pipeline for external API services (Claude, OpenAI).
    /// Includes:
    /// - Retry with exponential backoff (3 attempts, 1s-5s-15s delays)
    /// - Circuit breaker (opens after 50% failures in 30s window, stays open for 60s)
    /// </summary>
    public static ResiliencePipeline CreateExternalServicePipeline(
        string serviceName,
        ILogger logger)
    {
        return new ResiliencePipelineBuilder()
            // Retry with exponential backoff
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutException>()
                    .Handle<InvalidOperationException>(ex =>
                        ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                        ex.Message.Contains("overloaded", StringComparison.OrdinalIgnoreCase)),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "{Service} call failed (attempt {Attempt}/{Max}), retrying in {Delay:F1}s: {Error}",
                        serviceName,
                        args.AttemptNumber,
                        args.AttemptNumber + 1, // MaxRetryAttempts
                        args.RetryDelay.TotalSeconds,
                        args.Outcome.Exception?.Message ?? "Unknown error");
                    return default;
                }
            })
            // Circuit breaker - trip after sustained failures
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(60),
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutException>(),
                OnOpened = args =>
                {
                    logger.LogError(
                        "Circuit breaker OPENED for {Service}. Service calls will fail fast for {Duration}s. " +
                        "Last error: {Error}",
                        serviceName,
                        args.BreakDuration.TotalSeconds,
                        args.Outcome.Exception?.Message ?? "Unknown");
                    return default;
                },
                OnClosed = args =>
                {
                    logger.LogInformation(
                        "Circuit breaker CLOSED for {Service}. Service restored to normal operation.",
                        serviceName);
                    return default;
                },
                OnHalfOpened = args =>
                {
                    logger.LogInformation(
                        "Circuit breaker HALF-OPEN for {Service}. Testing if service has recovered...",
                        serviceName);
                    return default;
                }
            })
            .Build();
    }

    /// <summary>
    /// Creates a resilience pipeline that returns a typed result.
    /// Use this for operations that return values.
    /// </summary>
    public static ResiliencePipeline<T> CreateExternalServicePipeline<T>(
        string serviceName,
        ILogger logger)
    {
        return new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<T>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutException>()
                    .Handle<InvalidOperationException>(ex =>
                        ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                        ex.Message.Contains("overloaded", StringComparison.OrdinalIgnoreCase)),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "{Service} call failed (attempt {Attempt}), retrying in {Delay:F1}s: {Error}",
                        serviceName,
                        args.AttemptNumber,
                        args.RetryDelay.TotalSeconds,
                        args.Outcome.Exception?.Message ?? "Unknown error");
                    return default;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<T>
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(60),
                ShouldHandle = new PredicateBuilder<T>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutException>(),
                OnOpened = args =>
                {
                    logger.LogError(
                        "Circuit breaker OPENED for {Service}. Failing fast for {Duration}s.",
                        serviceName,
                        args.BreakDuration.TotalSeconds);
                    return default;
                },
                OnClosed = args =>
                {
                    logger.LogInformation("Circuit breaker CLOSED for {Service}.", serviceName);
                    return default;
                },
                OnHalfOpened = args =>
                {
                    logger.LogInformation("Circuit breaker HALF-OPEN for {Service}.", serviceName);
                    return default;
                }
            })
            .Build();
    }
}
