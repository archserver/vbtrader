using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VBTrader.Core.Interfaces;
using VBTrader.Core.Models;
using VBTrader.Infrastructure.Database.Entities;

namespace VBTrader.Infrastructure.Database;

public class PostgreSqlDataService : IDataService, IDisposable
{
    private readonly VBTraderDbContext _context;
    private readonly ILogger<PostgreSqlDataService> _logger;

    public PostgreSqlDataService(VBTraderDbContext context, ILogger<PostgreSqlDataService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task WriteStockQuoteAsync(StockQuote quote)
    {
        try
        {
            var entity = StockQuoteEntity.FromDomainModel(quote);
            _context.StockQuotes.Add(entity);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing stock quote for {Symbol}", quote.Symbol);
            throw;
        }
    }

    public async Task WriteBatchStockQuotesAsync(IEnumerable<StockQuote> quotes)
    {
        if (!quotes.Any()) return;

        try
        {
            var entities = quotes.Select(StockQuoteEntity.FromDomainModel).ToList();

            // Use AddRange for better performance with large batches
            _context.StockQuotes.AddRange(entities);
            await _context.SaveChangesAsync();

            _logger.LogDebug("Wrote {Count} stock quotes to PostgreSQL", entities.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing batch stock quotes");
            throw;
        }
    }

    public async Task WriteCandlestickDataAsync(CandlestickData candlestick, TimeFrame timeFrame = TimeFrame.OneMinute)
    {
        try
        {
            var entity = CandlestickDataEntity.FromDomainModel(candlestick, timeFrame);
            _context.CandlestickData.Add(entity);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing candlestick data for {Symbol}", candlestick.Symbol);
            throw;
        }
    }

    public async Task WriteMarketOpportunityAsync(MarketOpportunity opportunity)
    {
        try
        {
            var entity = MarketOpportunityEntity.FromDomainModel(opportunity);
            _context.MarketOpportunities.Add(entity);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing market opportunity for {Symbol}", opportunity.Symbol);
            throw;
        }
    }

    public async Task<IEnumerable<StockQuote>> GetRecentQuotesAsync(string symbol, TimeSpan timeRange)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow - timeRange;

            var entities = await _context.StockQuotes
                .Where(q => q.Symbol == symbol && q.Timestamp >= cutoffTime)
                .OrderByDescending(q => q.Timestamp)
                .Take(1000) // Limit to prevent excessive memory usage
                .ToListAsync();

            return entities.Select(e => e.ToDomainModel());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent quotes for {Symbol}", symbol);
            return Enumerable.Empty<StockQuote>();
        }
    }

    public async Task<IEnumerable<CandlestickData>> GetCandlestickDataAsync(string symbol, TimeFrame timeFrame, DateTime startTime, DateTime endTime)
    {
        try
        {
            var timeFrameMinutes = (int)timeFrame;

            var entities = await _context.CandlestickData
                .Where(c => c.Symbol == symbol
                    && c.TimeFrameMinutes == timeFrameMinutes
                    && c.Timestamp >= startTime
                    && c.Timestamp <= endTime)
                .OrderBy(c => c.Timestamp)
                .ToListAsync();

            return entities.Select(e => e.ToDomainModel());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting candlestick data for {Symbol}", symbol);
            return Enumerable.Empty<CandlestickData>();
        }
    }

    public async Task<IEnumerable<StockQuote>> GetTopMoversAsync(int limit, bool isPreMarket, MoversSort sortBy = MoversSort.PercentChangeUp)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-1); // Last hour

            var query = _context.StockQuotes
                .Where(q => q.IsPreMarket == isPreMarket && q.Timestamp >= cutoffTime);

            // Get the latest quote for each symbol
            var latestQuotes = query
                .GroupBy(q => q.Symbol)
                .Select(g => g.OrderByDescending(q => q.Timestamp).First());

            // Apply sorting
            var sortedQuotes = sortBy switch
            {
                MoversSort.Volume => latestQuotes.OrderByDescending(q => q.Volume),
                MoversSort.PercentChangeUp => latestQuotes.OrderByDescending(q => q.ChangePercent),
                MoversSort.PercentChangeDown => latestQuotes.OrderBy(q => q.ChangePercent),
                MoversSort.Trades => latestQuotes.OrderByDescending(q => q.Volume), // Approximate with volume
                _ => latestQuotes.OrderByDescending(q => q.ChangePercent)
            };

            var entities = await sortedQuotes.Take(limit).ToListAsync();
            return entities.Select(e => e.ToDomainModel());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting top movers");
            return Enumerable.Empty<StockQuote>();
        }
    }

    public async Task<IEnumerable<MarketOpportunity>> GetRecentOpportunitiesAsync(TimeSpan timeRange, int limit = 50)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow - timeRange;

            var entities = await _context.MarketOpportunities
                .Where(o => o.Timestamp >= cutoffTime)
                .OrderByDescending(o => o.Score)
                .ThenByDescending(o => o.Timestamp)
                .Take(limit)
                .ToListAsync();

            return entities.Select(e => e.ToDomainModel());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent opportunities");
            return Enumerable.Empty<MarketOpportunity>();
        }
    }

    public async Task<StockQuote?> GetLatestQuoteAsync(string symbol)
    {
        try
        {
            var entity = await _context.StockQuotes
                .Where(q => q.Symbol == symbol)
                .OrderByDescending(q => q.Timestamp)
                .FirstOrDefaultAsync();

            return entity?.ToDomainModel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest quote for {Symbol}", symbol);
            return null;
        }
    }

    public async Task<IEnumerable<string>> GetActiveSymbolsAsync(TimeSpan timeRange, int limit = 100)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow - timeRange;

            var symbols = await _context.StockQuotes
                .Where(q => q.Timestamp >= cutoffTime)
                .GroupBy(q => q.Symbol)
                .OrderByDescending(g => g.Count()) // Most active by update frequency
                .Take(limit)
                .Select(g => g.Key)
                .ToListAsync();

            return symbols;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active symbols");
            return Enumerable.Empty<string>();
        }
    }

    public async Task CleanupOldDataAsync(DateTime cutoffDate)
    {
        try
        {
            _logger.LogInformation("Starting data cleanup for data older than {CutoffDate}", cutoffDate);

            // Clean up stock quotes
            var oldQuotes = await _context.StockQuotes
                .Where(q => q.Timestamp < cutoffDate)
                .CountAsync();

            if (oldQuotes > 0)
            {
                // Use raw SQL for better performance on large deletes
                var quotesDeleted = await _context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM stock_quotes WHERE timestamp < {0}", cutoffDate);
                _logger.LogInformation("Deleted {Count} old stock quotes", quotesDeleted);
            }

            // Clean up candlestick data
            var candlesticksDeleted = await _context.Database.ExecuteSqlRawAsync(
                "DELETE FROM candlestick_data WHERE timestamp < {0}", cutoffDate);
            _logger.LogInformation("Deleted {Count} old candlestick records", candlesticksDeleted);

            // Clean up old opportunities (keep longer for analysis)
            var opportunityCutoff = cutoffDate.AddDays(-7); // Keep opportunities longer
            var opportunitiesDeleted = await _context.Database.ExecuteSqlRawAsync(
                "DELETE FROM market_opportunities WHERE timestamp < {0}", opportunityCutoff);
            _logger.LogInformation("Deleted {Count} old opportunity records", opportunitiesDeleted);

            _logger.LogInformation("Data cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during data cleanup");
        }
    }

    public async Task<Dictionary<string, object>> GetDatabaseStatsAsync()
    {
        try
        {
            var stats = new Dictionary<string, object>();

            // Count records in each table
            stats["TotalStockQuotes"] = await _context.StockQuotes.CountAsync();
            stats["TotalCandlestickData"] = await _context.CandlestickData.CountAsync();
            stats["TotalMarketOpportunities"] = await _context.MarketOpportunities.CountAsync();

            // Recent activity (last 24 hours)
            var yesterday = DateTime.UtcNow.AddDays(-1);
            stats["RecentQuotes"] = await _context.StockQuotes
                .CountAsync(q => q.Timestamp >= yesterday);
            stats["RecentOpportunities"] = await _context.MarketOpportunities
                .CountAsync(o => o.Timestamp >= yesterday);

            // Unique symbols
            stats["UniqueSymbols"] = await _context.StockQuotes
                .Select(q => q.Symbol)
                .Distinct()
                .CountAsync();

            // Database size (PostgreSQL specific)
            var sizeQuery = "SELECT pg_size_pretty(pg_database_size(current_database())) as size";
            var sizeResult = await _context.Database.SqlQueryRaw<string>(sizeQuery).FirstOrDefaultAsync();
            stats["DatabaseSize"] = sizeResult ?? "Unknown";

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting database statistics");
            return new Dictionary<string, object> { { "Error", ex.Message } };
        }
    }

    public async Task OptimizeDatabaseAsync()
    {
        try
        {
            _logger.LogInformation("Starting database optimization");

            // VACUUM and ANALYZE tables for better performance
            await _context.Database.ExecuteSqlRawAsync("VACUUM ANALYZE stock_quotes");
            await _context.Database.ExecuteSqlRawAsync("VACUUM ANALYZE candlestick_data");
            await _context.Database.ExecuteSqlRawAsync("VACUUM ANALYZE market_opportunities");

            // Update table statistics
            await _context.Database.ExecuteSqlRawAsync("ANALYZE");

            _logger.LogInformation("Database optimization completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during database optimization");
        }
    }

    public void Dispose()
    {
        _context?.Dispose();
    }
}

// Interface for the data service
public interface IDataService
{
    Task WriteStockQuoteAsync(StockQuote quote);
    Task WriteBatchStockQuotesAsync(IEnumerable<StockQuote> quotes);
    Task WriteCandlestickDataAsync(CandlestickData candlestick, TimeFrame timeFrame = TimeFrame.OneMinute);
    Task WriteMarketOpportunityAsync(MarketOpportunity opportunity);
    Task<IEnumerable<StockQuote>> GetRecentQuotesAsync(string symbol, TimeSpan timeRange);
    Task<IEnumerable<CandlestickData>> GetCandlestickDataAsync(string symbol, TimeFrame timeFrame, DateTime startTime, DateTime endTime);
    Task<IEnumerable<StockQuote>> GetTopMoversAsync(int limit, bool isPreMarket, MoversSort sortBy = MoversSort.PercentChangeUp);
    Task<IEnumerable<MarketOpportunity>> GetRecentOpportunitiesAsync(TimeSpan timeRange, int limit = 50);
    Task<StockQuote?> GetLatestQuoteAsync(string symbol);
    Task<IEnumerable<string>> GetActiveSymbolsAsync(TimeSpan timeRange, int limit = 100);
    Task CleanupOldDataAsync(DateTime cutoffDate);
    Task<Dictionary<string, object>> GetDatabaseStatsAsync();
    Task OptimizeDatabaseAsync();
}