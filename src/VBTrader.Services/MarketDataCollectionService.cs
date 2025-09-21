using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VBTrader.Core.Interfaces;
using VBTrader.Core.Models;
using VBTrader.Infrastructure.Database;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace VBTrader.Services;

public class MarketDataCollectionService : BackgroundService
{
    private readonly ISchwabApiClient _schwabClient;
    private readonly IDataService _dataService;
    private readonly ILogger<MarketDataCollectionService> _logger;
    private readonly MarketSettings _settings;

    private readonly Subject<StockQuote> _quoteStream = new();
    private readonly Subject<MarketOpportunity> _opportunityStream = new();

    private Timer? _preMarketTimer;
    private Timer? _marketHoursTimer;
    private Timer? _opportunityScanTimer;
    private Timer? _tickerDiscoveryTimer;
    private Timer? _cleanupTimer;

    private readonly HashSet<string> _watchedSymbols = new();
    private readonly HashSet<string> _activeSymbols = new();
    private bool _isMarketHours = false;

    public IObservable<StockQuote> QuoteStream => _quoteStream.AsObservable();
    public IObservable<MarketOpportunity> OpportunityStream => _opportunityStream.AsObservable();

    public MarketDataCollectionService(
        ISchwabApiClient schwabClient,
        IDataService dataService,
        ILogger<MarketDataCollectionService> logger,
        MarketSettings settings)
    {
        _schwabClient = schwabClient;
        _dataService = dataService;
        _logger = logger;
        _settings = settings;

        // Initialize with some default symbols
        InitializeDefaultSymbols();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Market Data Collection Service starting...");

        // Set up timers for different data collection schedules
        SetupTimers();

        // Setup data retention cleanup
        SetupCleanupTimer();

        // Wait for cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Market Data Collection Service stopping...");
        }
        finally
        {
            DisposeTimers();
        }
    }

    private void SetupTimers()
    {
        // Pre-market data collection (5x per second = every 200ms)
        _preMarketTimer = new Timer(
            async _ => await CollectPreMarketDataAsync(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(_settings.PreMarketUpdateIntervalMs));

        // Market hours data collection (20x per second = every 50ms)
        _marketHoursTimer = new Timer(
            async _ => await CollectMarketHoursDataAsync(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(_settings.MarketHoursUpdateIntervalMs));

        // Opportunity scanning (every minute)
        _opportunityScanTimer = new Timer(
            async _ => await ScanForOpportunitiesAsync(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(_settings.OpportunityScaniIntervalMs));

        // Ticker discovery (every hour)
        _tickerDiscoveryTimer = new Timer(
            async _ => await DiscoverNewTickersAsync(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(_settings.TickerDiscoveryIntervalMs));
    }

    private void SetupCleanupTimer()
    {
        // Cleanup old data daily at 2 AM
        var now = DateTime.Now;
        var nextCleanup = new DateTime(now.Year, now.Month, now.Day, 2, 0, 0);
        if (nextCleanup <= now)
            nextCleanup = nextCleanup.AddDays(1);

        var timeUntilCleanup = nextCleanup - now;

        _cleanupTimer = new Timer(
            async _ => await CleanupOldDataAsync(),
            null,
            timeUntilCleanup,
            TimeSpan.FromDays(1));
    }

    private async Task CollectPreMarketDataAsync()
    {
        if (_isMarketHours || !IsPreMarketHours())
            return;

        try
        {
            var quotes = await _schwabClient.GetQuotesAsync(_watchedSymbols);
            var preMarketQuotes = quotes.Select(q =>
            {
                q.IsPreMarket = true;
                return q;
            }).ToList();

            if (preMarketQuotes.Any())
            {
                await _dataService.WriteBatchStockQuotesAsync(preMarketQuotes);

                // Emit quotes to real-time stream
                foreach (var quote in preMarketQuotes)
                {
                    _quoteStream.OnNext(quote);
                }

                _logger.LogDebug("Collected {Count} pre-market quotes", preMarketQuotes.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting pre-market data");
        }
    }

    private async Task CollectMarketHoursDataAsync()
    {
        if (!_isMarketHours || !IsMarketHours())
            return;

        try
        {
            // Collect data for active symbols at higher frequency
            var quotes = await _schwabClient.GetQuotesAsync(_activeSymbols);
            var marketQuotes = quotes.Select(q =>
            {
                q.IsPreMarket = false;
                return q;
            }).ToList();

            if (marketQuotes.Any())
            {
                await _dataService.WriteBatchStockQuotesAsync(marketQuotes);

                // Emit quotes to real-time stream
                foreach (var quote in marketQuotes)
                {
                    _quoteStream.OnNext(quote);
                }

                _logger.LogDebug("Collected {Count} market hours quotes", marketQuotes.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting market hours data");
        }
    }

    private async Task ScanForOpportunitiesAsync()
    {
        try
        {
            _logger.LogDebug("Scanning for market opportunities...");

            // Get top movers by volume
            var volumeMovers = await _schwabClient.GetMoversAsync("$COMPX", MoversSort.Volume, 60);
            await ProcessMoversForOpportunities(volumeMovers, OpportunityType.VolumeSpike);

            // Get top percentage gainers
            var percentageMovers = await _schwabClient.GetMoversAsync("$COMPX", MoversSort.PercentChangeUp, 60);
            await ProcessMoversForOpportunities(percentageMovers, OpportunityType.PreMarketMover);

            // Update active symbols based on opportunities
            await UpdateActiveSymbols();

            _logger.LogDebug("Opportunity scan completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning for opportunities");
        }
    }

    private async Task ProcessMoversForOpportunities(IEnumerable<StockQuote> movers, OpportunityType type)
    {
        foreach (var quote in movers.Take(50)) // Process top 50
        {
            if (MeetsFilterCriteria(quote))
            {
                var opportunity = new MarketOpportunity
                {
                    Symbol = quote.Symbol,
                    OpportunityType = type,
                    Score = CalculateOpportunityScore(quote, type),
                    VolumeChange = CalculateVolumeChange(quote),
                    PriceChangePercent = quote.ChangePercent,
                    NewsSentiment = quote.NewsRating,
                    Confidence = CalculateConfidence(quote),
                    Reason = GenerateOpportunityReason(quote, type)
                };

                if (opportunity.Score >= 70) // Minimum score threshold
                {
                    await _dataService.WriteMarketOpportunityAsync(opportunity);
                    _opportunityStream.OnNext(opportunity);

                    // Add to watched symbols if not already present
                    _watchedSymbols.Add(quote.Symbol);
                }
            }
        }
    }

    private async Task DiscoverNewTickersAsync()
    {
        try
        {
            _logger.LogDebug("Discovering new tickers...");

            // Get movers from different indices to discover new symbols
            var indices = new[] { "$COMPX", "$DJI", "$SPX", "NYSE", "NASDAQ" };

            foreach (var index in indices)
            {
                try
                {
                    var movers = await _schwabClient.GetMoversAsync(index, MoversSort.Volume, 0);

                    foreach (var quote in movers.Take(20)) // Top 20 from each index
                    {
                        if (MeetsFilterCriteria(quote) && !_watchedSymbols.Contains(quote.Symbol))
                        {
                            _watchedSymbols.Add(quote.Symbol);
                            _logger.LogDebug("Added new symbol to watch list: {Symbol}", quote.Symbol);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error getting movers for index {Index}", index);
                }
            }

            _logger.LogInformation("Watch list now contains {Count} symbols", _watchedSymbols.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering new tickers");
        }
    }

    private async Task UpdateActiveSymbols()
    {
        try
        {
            // Get recent opportunities and update active symbols for high-frequency monitoring
            var topOpportunities = await _dataService.GetTopMoversAsync(10, IsPreMarketHours(), MoversSort.PercentChangeUp);

            _activeSymbols.Clear();
            foreach (var quote in topOpportunities.Take(3)) // Top 3 for active trading
            {
                _activeSymbols.Add(quote.Symbol);
            }

            _logger.LogDebug("Updated active symbols: {Symbols}", string.Join(", ", _activeSymbols));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating active symbols");
        }
    }

    private async Task CleanupOldDataAsync()
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-_settings.DataRetentionDays);
            await _dataService.CleanupOldDataAsync(cutoffDate);
            _logger.LogInformation("Cleaned up data older than {Days} days", _settings.DataRetentionDays);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during data cleanup");
        }
    }

    private bool MeetsFilterCriteria(StockQuote quote)
    {
        // Price filters
        if (quote.LastPrice < _settings.MinPrice || quote.LastPrice > _settings.MaxPrice)
            return false;

        // Volume filters
        var minVolume = quote.IsPreMarket ? _settings.MinPreMarketVolume : _settings.MinVolume;
        if (quote.Volume < minVolume)
            return false;

        // Float filters
        if (quote.SharesFloat < _settings.MinFloat || quote.SharesFloat > _settings.MaxFloat)
            return false;

        // Pre-market increase filter
        if (quote.IsPreMarket)
        {
            var increase = Math.Abs(quote.PreMarketChangePercent);
            if (increase < _settings.MinPreMarketIncrease || increase > _settings.MaxPreMarketIncrease)
                return false;
        }

        // Market cap filters
        var marketCapMillion = quote.MarketCap / 1000000;
        if (_settings.EnableSmallCap && marketCapMillion >= _settings.SmallCapMin && marketCapMillion <= _settings.SmallCapMax)
            return true;
        if (_settings.EnableMidCap && marketCapMillion >= _settings.MidCapMin && marketCapMillion <= _settings.MidCapMax)
            return true;
        if (_settings.EnableLargeCap && marketCapMillion >= _settings.LargeCapMin)
            return true;

        return false;
    }

    private decimal CalculateOpportunityScore(StockQuote quote, OpportunityType type)
    {
        decimal score = 0;

        // Base score from price movement
        score += Math.Abs(quote.ChangePercent) * 2;

        // Volume score
        var volumeScore = Math.Min(quote.Volume / 1000000m, 10) * 5; // Up to 50 points for volume
        score += volumeScore;

        // News sentiment bonus
        score += (int)quote.NewsRating * 5;

        // Type-specific scoring
        switch (type)
        {
            case OpportunityType.VolumeSpike:
                score += volumeScore * 0.5m; // Extra weight for volume
                break;
            case OpportunityType.PreMarketMover:
                score += Math.Abs(quote.ChangePercent); // Extra weight for price movement
                break;
        }

        return Math.Min(score, 100); // Cap at 100
    }

    private decimal CalculateVolumeChange(StockQuote quote)
    {
        // This would need historical data to calculate properly
        // For now, return the current volume as a percentage of average
        return quote.Volume / 1000000m; // Simplified calculation
    }

    private decimal CalculateConfidence(StockQuote quote)
    {
        decimal confidence = 50; // Base confidence

        // Increase confidence based on volume
        if (quote.Volume > 1000000) confidence += 10;
        if (quote.Volume > 5000000) confidence += 10;

        // Increase confidence based on price movement consistency
        if (Math.Abs(quote.ChangePercent) > 5) confidence += 10;
        if (Math.Abs(quote.ChangePercent) > 10) confidence += 10;

        // News sentiment impact
        if (quote.NewsRating >= NewsRating.Good) confidence += 10;

        return Math.Min(confidence, 100);
    }

    private string GenerateOpportunityReason(StockQuote quote, OpportunityType type)
    {
        var reasons = new List<string>();

        if (Math.Abs(quote.ChangePercent) > 10)
            reasons.Add($"{quote.ChangePercent:F1}% price movement");

        if (quote.Volume > 5000000)
            reasons.Add($"High volume: {quote.Volume:N0}");

        if (quote.NewsRating >= NewsRating.Good)
            reasons.Add($"Positive news sentiment: {quote.NewsRating}");

        return string.Join(", ", reasons);
    }

    private bool IsPreMarketHours()
    {
        var now = DateTime.Now;
        var easternTime = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
        return easternTime.Hour >= 4 && easternTime.Hour < 9 ||
               (easternTime.Hour == 9 && easternTime.Minute < 30);
    }

    private bool IsMarketHours()
    {
        var now = DateTime.Now;
        var easternTime = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));

        // Check if it's a weekday
        if (easternTime.DayOfWeek == DayOfWeek.Saturday || easternTime.DayOfWeek == DayOfWeek.Sunday)
            return false;

        // Market hours: 9:30 AM to 4:00 PM ET
        var marketOpen = new TimeSpan(9, 30, 0);
        var marketClose = new TimeSpan(16, 0, 0);

        return easternTime.TimeOfDay >= marketOpen && easternTime.TimeOfDay <= marketClose;
    }

    private void InitializeDefaultSymbols()
    {
        // Add some popular symbols to start with
        var defaultSymbols = new[]
        {
            "AAPL", "MSFT", "GOOGL", "AMZN", "NVDA", "TSLA", "META", "NFLX", "AMD", "INTC",
            "JPM", "JNJ", "V", "PG", "UNH", "DIS", "ADBE", "PYPL", "CMCSA", "PFE"
        };

        foreach (var symbol in defaultSymbols)
        {
            _watchedSymbols.Add(symbol);
        }

        _logger.LogInformation("Initialized with {Count} default symbols", _watchedSymbols.Count);
    }

    private void DisposeTimers()
    {
        _preMarketTimer?.Dispose();
        _marketHoursTimer?.Dispose();
        _opportunityScanTimer?.Dispose();
        _tickerDiscoveryTimer?.Dispose();
        _cleanupTimer?.Dispose();
    }

    public override void Dispose()
    {
        DisposeTimers();
        _quoteStream.Dispose();
        _opportunityStream.Dispose();
        base.Dispose();
    }

    // Public methods for manual control
    public async Task AddSymbolToWatchListAsync(string symbol)
    {
        _watchedSymbols.Add(symbol.ToUpper());
        _logger.LogInformation("Added {Symbol} to watch list", symbol);
    }

    public async Task RemoveSymbolFromWatchListAsync(string symbol)
    {
        _watchedSymbols.Remove(symbol.ToUpper());
        _logger.LogInformation("Removed {Symbol} from watch list", symbol);
    }

    public IEnumerable<string> GetWatchedSymbols() => _watchedSymbols.ToList();
    public IEnumerable<string> GetActiveSymbols() => _activeSymbols.ToList();
}