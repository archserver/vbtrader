using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
// TODO: Add Microsoft.Extensions.Logging package reference
// using Microsoft.Extensions.Logging;

namespace VBTrader.Core.Services;

// TODO: Temporarily commented out - need logging package reference
/*
public class RateLimiter
{
    private readonly ILogger<RateLimiter> _logger;
    private readonly ConcurrentQueue<DateTime> _requestTimestamps;
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxRequests;
    private readonly TimeSpan _timeWindow;
    private readonly string _name;

    public RateLimiter(ILogger<RateLimiter> logger, string name, int maxRequests, TimeSpan timeWindow)
    {
        _logger = logger;
        _name = name;
        _maxRequests = maxRequests;
        _timeWindow = timeWindow;
        _requestTimestamps = new ConcurrentQueue<DateTime>();
        _semaphore = new SemaphoreSlim(1, 1);
    }

    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var windowStart = now - _timeWindow;

            // Clean up old timestamps
            while (_requestTimestamps.TryPeek(out var timestamp) && timestamp < windowStart)
            {
                _requestTimestamps.TryDequeue(out _);
            }

            // Check if we can make a request
            if (_requestTimestamps.Count >= _maxRequests)
            {
                // Calculate when we can make the next request
                if (_requestTimestamps.TryPeek(out var oldestTimestamp))
                {
                    var waitTime = (oldestTimestamp + _timeWindow) - now;
                    if (waitTime > TimeSpan.Zero)
                    {
                        _logger.LogWarning($"Rate limit reached for {_name}. Waiting {waitTime.TotalSeconds:F1} seconds");
                        await Task.Delay(waitTime, cancellationToken);
                        return await TryAcquireAsync(cancellationToken); // Retry
                    }
                }
            }

            // Record this request
            _requestTimestamps.Enqueue(now);
            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public int GetCurrentRequestCount()
    {
        var now = DateTime.UtcNow;
        var windowStart = now - _timeWindow;

        // Count valid timestamps
        return _requestTimestamps.Count(t => t >= windowStart);
    }

    public TimeSpan GetTimeUntilNextRequest()
    {
        if (_requestTimestamps.Count < _maxRequests)
            return TimeSpan.Zero;

        if (_requestTimestamps.TryPeek(out var oldestTimestamp))
        {
            var nextAvailable = oldestTimestamp + _timeWindow;
            var waitTime = nextAvailable - DateTime.UtcNow;
            return waitTime > TimeSpan.Zero ? waitTime : TimeSpan.Zero;
        }

        return TimeSpan.Zero;
    }
}

public class SchwabApiRateLimiter
{
    private readonly RateLimiter _tradingApiLimiter;
    private readonly RateLimiter _marketDataApiLimiter;
    private readonly RateLimiter _overallApiLimiter;
    private readonly ILogger<SchwabApiRateLimiter> _logger;

    public SchwabApiRateLimiter(ILogger<SchwabApiRateLimiter> logger)
    {
        _logger = logger;

        // Schwab API limits (adjust based on actual limits)
        // Trading API: 120 requests per minute
        _tradingApiLimiter = new RateLimiter(
            logger.CreateLogger<RateLimiter>(),
            "Trading API",
            120,
            TimeSpan.FromMinutes(1));

        // Market Data API: 120 requests per minute
        _marketDataApiLimiter = new RateLimiter(
            logger.CreateLogger<RateLimiter>(),
            "Market Data API",
            120,
            TimeSpan.FromMinutes(1));

        // Overall API: 10,000 requests per hour
        _overallApiLimiter = new RateLimiter(
            logger.CreateLogger<RateLimiter>(),
            "Overall API",
            10000,
            TimeSpan.FromHours(1));
    }

    public async Task<bool> WaitForTradingApiAsync(CancellationToken cancellationToken = default)
    {
        // Check both trading-specific and overall limits
        await _overallApiLimiter.TryAcquireAsync(cancellationToken);
        return await _tradingApiLimiter.TryAcquireAsync(cancellationToken);
    }

    public async Task<bool> WaitForMarketDataApiAsync(CancellationToken cancellationToken = default)
    {
        // Check both market-data-specific and overall limits
        await _overallApiLimiter.TryAcquireAsync(cancellationToken);
        return await _marketDataApiLimiter.TryAcquireAsync(cancellationToken);
    }

    public void LogCurrentStatus()
    {
        var tradingCount = _tradingApiLimiter.GetCurrentRequestCount();
        var marketCount = _marketDataApiLimiter.GetCurrentRequestCount();
        var overallCount = _overallApiLimiter.GetCurrentRequestCount();

        _logger.LogInformation($"API Rate Limits - Trading: {tradingCount}/120 per min, " +
                              $"Market: {marketCount}/120 per min, " +
                              $"Overall: {overallCount}/10000 per hour");
    }

    public RateLimitStatus GetStatus()
    {
        return new RateLimitStatus
        {
            TradingApiRequestsPerMinute = _tradingApiLimiter.GetCurrentRequestCount(),
            MarketDataApiRequestsPerMinute = _marketDataApiLimiter.GetCurrentRequestCount(),
            OverallApiRequestsPerHour = _overallApiLimiter.GetCurrentRequestCount(),
            TradingApiWaitTime = _tradingApiLimiter.GetTimeUntilNextRequest(),
            MarketDataApiWaitTime = _marketDataApiLimiter.GetTimeUntilNextRequest(),
            OverallApiWaitTime = _overallApiLimiter.GetTimeUntilNextRequest()
        };
    }
}

public class RateLimitStatus
{
    public int TradingApiRequestsPerMinute { get; set; }
    public int MarketDataApiRequestsPerMinute { get; set; }
    public int OverallApiRequestsPerHour { get; set; }
    public TimeSpan TradingApiWaitTime { get; set; }
    public TimeSpan MarketDataApiWaitTime { get; set; }
    public TimeSpan OverallApiWaitTime { get; set; }

    public bool IsAtLimit => TradingApiWaitTime > TimeSpan.Zero ||
                             MarketDataApiWaitTime > TimeSpan.Zero ||
                             OverallApiWaitTime > TimeSpan.Zero;
}
*/ // End of temporarily commented RateLimiter classes