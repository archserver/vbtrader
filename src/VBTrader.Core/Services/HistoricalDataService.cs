using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// TODO: Add Entity Framework package reference to VBTrader.Core project
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Logging;
using VBTrader.Core.Interfaces;
using VBTrader.Core.Models;
// TODO: Add project reference to Infrastructure
// using VBTrader.Infrastructure.Database;
// using VBTrader.Infrastructure.Database.Entities;

namespace VBTrader.Core.Services;

// TODO: Temporarily commented out - need to add EF references to Core project
// Will be properly fixed after authentication testing
/*
public class HistoricalDataService
{
    private readonly ILogger<HistoricalDataService> _logger;
    private readonly ISchwabApiClient _schwabClient;
    private readonly VBTraderDbContext _dbContext;

    private const int MAX_TICKERS = 5;
    private readonly List<string> _defaultTickers = new() { "AAPL", "MSFT", "GOOGL", "AMZN", "TSLA" };

    public HistoricalDataService(
        ILogger<HistoricalDataService> logger,
        ISchwabApiClient schwabClient,
        VBTraderDbContext dbContext)
    {
        _logger = logger;
        _schwabClient = schwabClient;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Fetch historical data from Schwab and save to the same database used for live trading
    /// </summary>
    public async Task<HistoricalDataResult> FetchAndSaveHistoricalDataAsync(
        List<string> tickers,
        DateTime startDate,
        DateTime endDate,
        TimeFrame timeFrame = TimeFrame.OneDay)
    {
        var result = new HistoricalDataResult
        {
            RequestedTickers = tickers,
            StartDate = startDate,
            EndDate = endDate,
            TimeFrame = timeFrame
        };

        // Validate ticker count
        if (tickers.Count > MAX_TICKERS)
        {
            _logger.LogWarning($"Too many tickers requested ({tickers.Count}). Limiting to {MAX_TICKERS}");
            tickers = tickers.Take(MAX_TICKERS).ToList();
            result.Warnings.Add($"Ticker list limited to {MAX_TICKERS} symbols");
        }

        // Process each ticker
        foreach (var ticker in tickers)
        {
            var tickerResult = await ProcessTickerAsync(ticker, startDate, endDate, timeFrame);
            result.TickerResults.Add(tickerResult);
        }

        // Save all changes to database
        await _dbContext.SaveChangesAsync();

        // Calculate summary
        result.TotalDataPoints = result.TickerResults.Sum(r => r.DataPointsRetrieved);
        result.TotalSavedToDatabase = result.TickerResults.Sum(r => r.DataPointsSaved);
        result.Success = result.TickerResults.All(r => r.Success);

        _logger.LogInformation($"Historical data fetch complete. " +
            $"Retrieved {result.TotalDataPoints} data points, " +
            $"Saved {result.TotalSavedToDatabase} to database");

        return result;
    }

    private async Task<TickerDataResult> ProcessTickerAsync(
        string ticker,
        DateTime startDate,
        DateTime endDate,
        TimeFrame timeFrame)
    {
        var result = new TickerDataResult
        {
            Ticker = ticker,
            Success = false
        };

        try
        {
            _logger.LogInformation($"Fetching historical data for {ticker} from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");

            // Fetch data from Schwab API
            var priceHistory = await _schwabClient.GetPriceHistoryAsync(ticker, timeFrame, startDate, endDate);
            var dataPoints = priceHistory.ToList();

            result.DataPointsRetrieved = dataPoints.Count;
            result.OldestDataPoint = dataPoints.MinBy(d => d.Timestamp)?.Timestamp;
            result.NewestDataPoint = dataPoints.MaxBy(d => d.Timestamp)?.Timestamp;

            _logger.LogInformation($"Retrieved {dataPoints.Count} data points for {ticker}");

            if (dataPoints.Any())
            {
                // Save to the candlestick_data table (same table used for live trading)
                var savedCount = await SaveToCandlestickTable(dataPoints);

                // Also cache in historical_data_cache for quick access
                await CacheHistoricalData(dataPoints, timeFrame);

                result.DataPointsSaved = savedCount;
                _logger.LogInformation($"Saved {savedCount} data points for {ticker} to database");
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, $"Failed to process historical data for {ticker}");
        }

        return result;
    }

    private async Task<int> SaveToCandlestickTable(List<CandlestickData> dataPoints)
    {
        var savedCount = 0;

        foreach (var batch in dataPoints.Chunk(100))
        {
            foreach (var candle in batch)
            {
                // Check if this data point already exists
                var exists = await _dbContext.CandlestickData
                    .AnyAsync(c => c.Symbol == candle.Symbol &&
                                   c.Timestamp == candle.Timestamp);

                if (!exists)
                {
                    var entity = new CandlestickDataEntity
                    {
                        Symbol = candle.Symbol,
                        Timestamp = candle.Timestamp,
                        Open = candle.Open,
                        High = candle.High,
                        Low = candle.Low,
                        Close = candle.Close,
                        Volume = candle.Volume,
                        DataSource = "Schwab",
                        CreatedAt = DateTime.UtcNow
                    };

                    _dbContext.CandlestickData.Add(entity);
                    savedCount++;
                }
                else
                {
                    // Update existing record if needed
                    var existing = await _dbContext.CandlestickData
                        .FirstOrDefaultAsync(c => c.Symbol == candle.Symbol &&
                                                 c.Timestamp == candle.Timestamp);

                    if (existing != null)
                    {
                        existing.Open = candle.Open;
                        existing.High = candle.High;
                        existing.Low = candle.Low;
                        existing.Close = candle.Close;
                        existing.Volume = candle.Volume;
                        existing.UpdatedAt = DateTime.UtcNow;
                    }
                }
            }
        }

        return savedCount;
    }

    private async Task CacheHistoricalData(List<CandlestickData> dataPoints, TimeFrame timeFrame)
    {
        foreach (var candle in dataPoints)
        {
            // Check if already cached
            var exists = await _dbContext.HistoricalDataCache
                .AnyAsync(h => h.Symbol == candle.Symbol &&
                              h.Timestamp == candle.Timestamp &&
                              h.TimeFrame == timeFrame.ToString());

            if (!exists)
            {
                var cacheEntity = new HistoricalDataCacheEntity
                {
                    Symbol = candle.Symbol,
                    Timestamp = candle.Timestamp,
                    Open = candle.Open,
                    High = candle.High,
                    Low = candle.Low,
                    Close = candle.Close,
                    AdjustedClose = candle.Close, // Schwab provides adjusted close
                    Volume = candle.Volume,
                    TimeFrame = timeFrame.ToString(),
                    DataSource = "Schwab",
                    CachedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(7) // Cache for 7 days
                };

                _dbContext.HistoricalDataCache.Add(cacheEntity);
            }
        }
    }

    /// <summary>
    /// Get historical data from database (not from API)
    /// </summary>
    public async Task<List<CandlestickData>> GetHistoricalDataFromDatabaseAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate)
    {
        var data = await _dbContext.CandlestickData
            .Where(c => c.Symbol == symbol &&
                       c.Timestamp >= startDate &&
                       c.Timestamp <= endDate)
            .OrderBy(c => c.Timestamp)
            .Select(c => new CandlestickData
            {
                Symbol = c.Symbol,
                Timestamp = c.Timestamp,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume
            })
            .ToListAsync();

        return data;
    }

    /// <summary>
    /// Interactive ticker selection
    /// </summary>
    public async Task<List<string>> SelectTickersAsync()
    {
        var selectedTickers = new List<string>();

        Console.WriteLine("\n┌─────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│            Select Tickers for Historical Data (Max 5)           │");
        Console.WriteLine("└─────────────────────────────────────────────────────────────────┘\n");

        Console.WriteLine("Default tickers: " + string.Join(", ", _defaultTickers));
        Console.Write("\nUse default tickers? (yes/no): ");

        var useDefaults = Console.ReadLine()?.ToLower() == "yes";

        if (useDefaults)
        {
            selectedTickers = new List<string>(_defaultTickers);
        }
        else
        {
            Console.WriteLine($"\nEnter up to {MAX_TICKERS} ticker symbols (one per line, empty line to finish):");

            for (int i = 0; i < MAX_TICKERS; i++)
            {
                Console.Write($"Ticker {i + 1}: ");
                var ticker = Console.ReadLine()?.ToUpper();

                if (string.IsNullOrWhiteSpace(ticker))
                    break;

                // Validate ticker exists
                try
                {
                    var quote = await _schwabClient.GetQuoteAsync(ticker);
                    if (quote != null && quote.LastPrice > 0)
                    {
                        selectedTickers.Add(ticker);
                        Console.WriteLine($"✅ {ticker} validated");
                    }
                    else
                    {
                        Console.WriteLine($"❌ {ticker} not found");
                        i--; // Allow retry
                    }
                }
                catch
                {
                    Console.WriteLine($"❌ Could not validate {ticker}");
                    i--; // Allow retry
                }
            }
        }

        Console.WriteLine($"\nSelected tickers: {string.Join(", ", selectedTickers)}");
        return selectedTickers;
    }

    /// <summary>
    /// Get statistics about stored historical data
    /// </summary>
    public async Task<DataStatistics> GetDataStatisticsAsync()
    {
        var stats = new DataStatistics();

        // Get unique symbols
        stats.UniqueSymbols = await _dbContext.CandlestickData
            .Select(c => c.Symbol)
            .Distinct()
            .CountAsync();

        // Get total records
        stats.TotalRecords = await _dbContext.CandlestickData.CountAsync();

        // Get date range
        var dates = await _dbContext.CandlestickData
            .Select(c => c.Timestamp)
            .ToListAsync();

        if (dates.Any())
        {
            stats.OldestData = dates.Min();
            stats.NewestData = dates.Max();
        }

        // Get cached data count
        stats.CachedRecords = await _dbContext.HistoricalDataCache
            .Where(h => h.ExpiresAt > DateTime.UtcNow)
            .CountAsync();

        return stats;
    }
}

// Result models
public class HistoricalDataResult
{
    public List<string> RequestedTickers { get; set; } = new();
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public TimeFrame TimeFrame { get; set; }
    public List<TickerDataResult> TickerResults { get; set; } = new();
    public int TotalDataPoints { get; set; }
    public int TotalSavedToDatabase { get; set; }
    public bool Success { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class TickerDataResult
{
    public string Ticker { get; set; } = "";
    public int DataPointsRetrieved { get; set; }
    public int DataPointsSaved { get; set; }
    public DateTime? OldestDataPoint { get; set; }
    public DateTime? NewestDataPoint { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class DataStatistics
{
    public int UniqueSymbols { get; set; }
    public long TotalRecords { get; set; }
    public long CachedRecords { get; set; }
    public DateTime? OldestData { get; set; }
    public DateTime? NewestData { get; set; }
}
*/ // End of temporarily commented HistoricalDataService