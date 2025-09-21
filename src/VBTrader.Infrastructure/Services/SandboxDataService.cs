using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VBTrader.Core.Interfaces;
using VBTrader.Core.Models;
using VBTrader.Infrastructure.Database;
using VBTrader.Infrastructure.Database.Entities;

namespace VBTrader.Infrastructure.Services;

public class SandboxDataService : ISandboxDataService
{
    private readonly VBTraderDbContext _context;
    private readonly ILogger<SandboxDataService> _logger;
    private readonly Random _random = new();

    public SandboxDataService(VBTraderDbContext context, ILogger<SandboxDataService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<SandboxSession> CreateSandboxSessionAsync(int userId, DateTime startDate, DateTime endDate, decimal initialBalance = 100000m)
    {
        // End any existing active sessions
        var activeSessions = await _context.SandboxSessions
            .Where(s => s.UserId == userId && s.IsActive)
            .ToListAsync();

        foreach (var session in activeSessions)
        {
            session.IsActive = false;
            session.CompletedAt = DateTime.UtcNow;
        }

        var sandboxSession = new SandboxSessionEntity
        {
            UserId = userId,
            SessionName = $"Sandbox Session - {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}",
            StartDate = startDate,
            EndDate = endDate,
            CurrentTime = startDate,
            InitialBalance = initialBalance,
            CurrentBalance = initialBalance,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            WatchedSymbols = string.Join(",", new[] { "AAPL", "TSLA", "NVDA", "MSFT", "GOOGL" }),
            Settings = new SandboxSettingsEntity
            {
                AutoAdvanceTime = true,
                TimeAdvanceIntervalMinutes = 1,
                SkipWeekends = true,
                SkipHolidays = true,
                EnableSlippage = true,
                SlippagePercentage = 0.1m,
                EnableCommissions = false,
                CommissionPerTrade = 0m,
                MaxPositionsPerSymbol = 10000
            }
        };

        _context.SandboxSessions.Add(sandboxSession);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created sandbox session for user {UserId} from {StartDate} to {EndDate}",
            userId, startDate, endDate);

        return MapToSandboxSession(sandboxSession);
    }

    public async Task<SandboxSession?> GetActiveSandboxSessionAsync(int userId)
    {
        var session = await _context.SandboxSessions
            .Include(s => s.Settings)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.IsActive);

        return session != null ? MapToSandboxSession(session) : null;
    }

    public async Task<bool> EndSandboxSessionAsync(int sandboxSessionId)
    {
        var session = await _context.SandboxSessions.FindAsync(sandboxSessionId);
        if (session == null) return false;

        session.IsActive = false;
        session.CompletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Ended sandbox session {SandboxSessionId}", sandboxSessionId);
        return true;
    }

    public async Task<IEnumerable<SandboxSession>> GetSandboxSessionHistoryAsync(int userId)
    {
        var sessions = await _context.SandboxSessions
            .Include(s => s.Settings)
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return sessions.Select(MapToSandboxSession);
    }

    public async Task<IEnumerable<StockQuote>> GetHistoricalQuotesAsync(IEnumerable<string> symbols, DateTime timestamp)
    {
        // Get the closest historical data to the requested timestamp
        var targetDate = timestamp.Date;
        var quotes = new List<StockQuote>();

        foreach (var symbol in symbols)
        {
            var historicalData = await _context.StockQuotes
                .Where(q => q.Symbol == symbol && q.Timestamp.Date == targetDate)
                .OrderBy(q => Math.Abs((q.Timestamp - timestamp).Ticks))
                .FirstOrDefaultAsync();

            if (historicalData != null)
            {
                quotes.Add(MapToStockQuote(historicalData));
            }
            else
            {
                // Generate mock data if no historical data available
                quotes.Add(GenerateMockQuote(symbol, timestamp));
            }
        }

        return quotes;
    }

    public async Task<IEnumerable<StockQuote>> GetHistoricalQuotesForPeriodAsync(IEnumerable<string> symbols, DateTime startDate, DateTime endDate)
    {
        var quotes = new List<StockQuote>();

        foreach (var symbol in symbols)
        {
            var historicalData = await _context.StockQuotes
                .Where(q => q.Symbol == symbol && q.Timestamp >= startDate && q.Timestamp <= endDate)
                .OrderBy(q => q.Timestamp)
                .ToListAsync();

            quotes.AddRange(historicalData.Select(MapToStockQuote));
        }

        return quotes;
    }

    public async Task<bool> HasHistoricalDataAsync(string symbol, DateTime date)
    {
        return await _context.StockQuotes
            .AnyAsync(q => q.Symbol == symbol && q.Timestamp.Date == date.Date);
    }

    public async Task<DateRange> GetAvailableDataRangeAsync(string symbol)
    {
        var quotes = await _context.StockQuotes
            .Where(q => q.Symbol == symbol)
            .GroupBy(q => 1)
            .Select(g => new
            {
                StartDate = g.Min(q => q.Timestamp),
                EndDate = g.Max(q => q.Timestamp),
                TotalDays = g.Select(q => q.Timestamp.Date).Distinct().Count()
            })
            .FirstOrDefaultAsync();

        if (quotes == null)
        {
            return new DateRange
            {
                StartDate = DateTime.Today.AddDays(-30),
                EndDate = DateTime.Today,
                TotalDays = 30,
                TradingDays = 22
            };
        }

        return new DateRange
        {
            StartDate = quotes.StartDate,
            EndDate = quotes.EndDate,
            TotalDays = quotes.TotalDays,
            TradingDays = (int)(quotes.TotalDays * 0.71) // Approximate trading days
        };
    }

    public async Task<DateTime> GetSandboxCurrentTimeAsync(int sandboxSessionId)
    {
        var session = await _context.SandboxSessions.FindAsync(sandboxSessionId);
        return session?.CurrentTime ?? DateTime.UtcNow;
    }

    public async Task<bool> SetSandboxTimeAsync(int sandboxSessionId, DateTime sandboxTime)
    {
        var session = await _context.SandboxSessions.FindAsync(sandboxSessionId);
        if (session == null) return false;

        session.CurrentTime = sandboxTime;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> AdvanceSandboxTimeAsync(int sandboxSessionId, TimeSpan interval)
    {
        var session = await _context.SandboxSessions.FindAsync(sandboxSessionId);
        if (session == null) return false;

        var newTime = session.CurrentTime.Add(interval);
        if (newTime > session.EndDate) return false;

        session.CurrentTime = newTime;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<SandboxTimeStatus> GetSandboxTimeStatusAsync(int sandboxSessionId)
    {
        var session = await _context.SandboxSessions.FindAsync(sandboxSessionId);
        if (session == null)
        {
            return new SandboxTimeStatus
            {
                CurrentTime = DateTime.UtcNow,
                IsMarketOpen = false,
                CanAdvanceTime = false,
                StatusMessage = "Sandbox session not found"
            };
        }

        var marketHours = await GetMarketHoursAsync(sandboxSessionId, session.CurrentTime);
        var isMarketOpen = await IsMarketOpenAsync(sandboxSessionId);

        return new SandboxTimeStatus
        {
            CurrentTime = session.CurrentTime,
            IsMarketOpen = isMarketOpen,
            CurrentSession = GetCurrentMarketSession(session.CurrentTime, marketHours),
            NextMarketOpen = marketHours.MarketOpen,
            NextMarketClose = marketHours.MarketClose,
            CanAdvanceTime = session.CurrentTime < session.EndDate,
            StatusMessage = isMarketOpen ? "Market is open" : "Market is closed"
        };
    }

    public async Task<IEnumerable<StockQuote>> GetCurrentSandboxQuotesAsync(int sandboxSessionId, IEnumerable<string> symbols)
    {
        var currentTime = await GetSandboxCurrentTimeAsync(sandboxSessionId);
        return await GetHistoricalQuotesAsync(symbols, currentTime);
    }

    public async Task<IEnumerable<MarketOpportunity>> GetCurrentSandboxOpportunitiesAsync(int sandboxSessionId)
    {
        var currentTime = await GetSandboxCurrentTimeAsync(sandboxSessionId);

        // Generate mock opportunities based on current sandbox time
        var opportunities = new List<MarketOpportunity>
        {
            new() { Symbol = "AAPL", Score = 85.5m, OpportunityType = OpportunityType.BreakoutUp, PriceChangePercent = 1.33m },
            new() { Symbol = "TSLA", Score = 72.3m, OpportunityType = OpportunityType.VolumeSpike, PriceChangePercent = -1.28m },
            new() { Symbol = "NVDA", Score = 91.2m, OpportunityType = OpportunityType.TechnicalIndicator, PriceChangePercent = 2.01m }
        };

        return opportunities;
    }

    public async Task<bool> IsMarketOpenAsync(int sandboxSessionId)
    {
        var currentTime = await GetSandboxCurrentTimeAsync(sandboxSessionId);
        var marketHours = await GetMarketHoursAsync(sandboxSessionId, currentTime);

        return !marketHours.IsHoliday &&
               currentTime.TimeOfDay >= marketHours.MarketOpen.TimeOfDay &&
               currentTime.TimeOfDay <= marketHours.MarketClose.TimeOfDay &&
               currentTime.DayOfWeek != DayOfWeek.Saturday &&
               currentTime.DayOfWeek != DayOfWeek.Sunday;
    }

    public async Task<MarketHours> GetMarketHoursAsync(int sandboxSessionId, DateTime date)
    {
        // Standard US market hours
        var marketOpen = date.Date.AddHours(9).AddMinutes(30); // 9:30 AM ET
        var marketClose = date.Date.AddHours(16); // 4:00 PM ET

        return new MarketHours
        {
            Date = date.Date,
            PreMarketOpen = date.Date.AddHours(4), // 4:00 AM ET
            MarketOpen = marketOpen,
            MarketClose = marketClose,
            AfterHoursClose = date.Date.AddHours(20), // 8:00 PM ET
            IsHoliday = IsMarketHoliday(date),
            HolidayName = GetHolidayName(date)
        };
    }

    public async Task<SandboxTradeResult> ExecuteSandboxTradeAsync(int sandboxSessionId, string symbol, TradeAction action, int quantity, OrderType orderType, decimal? limitPrice = null)
    {
        var session = await _context.SandboxSessions.FindAsync(sandboxSessionId);
        if (session == null)
        {
            return new SandboxTradeResult
            {
                Success = false,
                ErrorMessage = "Sandbox session not found"
            };
        }

        var currentQuotes = await GetCurrentSandboxQuotesAsync(sandboxSessionId, new[] { symbol });
        var quote = currentQuotes.FirstOrDefault();
        if (quote == null)
        {
            return new SandboxTradeResult
            {
                Success = false,
                ErrorMessage = "Quote not available for symbol"
            };
        }

        // Calculate execution price with slippage
        var executionPrice = CalculateExecutionPrice(quote.LastPrice, action, orderType, limitPrice, session.Settings);
        var totalCost = executionPrice * quantity;
        var commission = session.Settings.EnableCommissions ? session.Settings.CommissionPerTrade : 0;

        // Check if sufficient funds for buy orders
        if (action == TradeAction.Buy && (totalCost + commission) > session.CurrentBalance)
        {
            return new SandboxTradeResult
            {
                Success = false,
                ErrorMessage = "Insufficient funds"
            };
        }

        // Record the trade
        var trade = new SandboxTradeEntity
        {
            SandboxSessionId = sandboxSessionId,
            Symbol = symbol,
            Action = action,
            Quantity = quantity,
            Price = executionPrice,
            TotalValue = totalCost,
            Commission = commission,
            ExecutedAt = session.CurrentTime,
            OrderType = orderType,
            HistoricalDataTimestamp = session.CurrentTime
        };

        _context.SandboxTrades.Add(trade);

        // Update session balance
        if (action == TradeAction.Buy)
        {
            session.CurrentBalance -= (totalCost + commission);
        }
        else
        {
            session.CurrentBalance += (totalCost - commission);
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Executed sandbox trade: {Action} {Quantity} {Symbol} at ${Price} for session {SessionId}",
            action, quantity, symbol, executionPrice, sandboxSessionId);

        return new SandboxTradeResult
        {
            Success = true,
            TradeId = trade.TradeId,
            ExecutionPrice = executionPrice,
            ExecutionTime = session.CurrentTime,
            Slippage = Math.Abs(executionPrice - quote.LastPrice),
            Commission = commission,
            TotalCost = totalCost + commission,
            NewBalance = session.CurrentBalance
        };
    }

    public async Task<IEnumerable<SandboxPosition>> GetSandboxPositionsAsync(int sandboxSessionId)
    {
        var trades = await _context.SandboxTrades
            .Where(t => t.SandboxSessionId == sandboxSessionId)
            .GroupBy(t => t.Symbol)
            .Select(g => new
            {
                Symbol = g.Key,
                NetQuantity = g.Sum(t => t.Action == TradeAction.Buy ? t.Quantity : -t.Quantity),
                AveragePrice = g.Where(t => t.Action == TradeAction.Buy).Average(t => t.Price),
                FirstPurchase = g.Where(t => t.Action == TradeAction.Buy).Min(t => t.ExecutedAt)
            })
            .Where(p => p.NetQuantity > 0)
            .ToListAsync();

        var positions = new List<SandboxPosition>();
        var symbols = trades.Select(t => t.Symbol);
        var currentQuotes = await GetCurrentSandboxQuotesAsync(sandboxSessionId, symbols);

        foreach (var trade in trades)
        {
            var quote = currentQuotes.FirstOrDefault(q => q.Symbol == trade.Symbol);
            var currentPrice = quote?.LastPrice ?? trade.AveragePrice;
            var marketValue = currentPrice * trade.NetQuantity;
            var costBasis = trade.AveragePrice * trade.NetQuantity;

            positions.Add(new SandboxPosition
            {
                Symbol = trade.Symbol,
                Quantity = trade.NetQuantity,
                AveragePrice = trade.AveragePrice,
                CurrentPrice = currentPrice,
                MarketValue = marketValue,
                UnrealizedPnL = marketValue - costBasis,
                UnrealizedPnLPercent = ((marketValue - costBasis) / costBasis) * 100,
                FirstPurchaseDate = trade.FirstPurchase,
                LastUpdateDate = DateTime.UtcNow
            });
        }

        return positions;
    }

    public async Task<decimal> GetSandboxAccountBalanceAsync(int sandboxSessionId)
    {
        var session = await _context.SandboxSessions.FindAsync(sandboxSessionId);
        return session?.CurrentBalance ?? 0;
    }

    public async Task<SandboxPerformanceReport> GetSandboxPerformanceAsync(int sandboxSessionId)
    {
        var session = await _context.SandboxSessions.FindAsync(sandboxSessionId);
        if (session == null)
        {
            return new SandboxPerformanceReport();
        }

        var trades = await _context.SandboxTrades
            .Where(t => t.SandboxSessionId == sandboxSessionId)
            .OrderBy(t => t.ExecutedAt)
            .ToListAsync();

        var positions = await GetSandboxPositionsAsync(sandboxSessionId);
        var totalMarketValue = positions.Sum(p => p.MarketValue);
        var totalUnrealizedPnL = positions.Sum(p => p.UnrealizedPnL);

        // Calculate realized P&L from completed trades
        var realizedPnL = CalculateRealizedPnL(trades);

        var totalTrades = trades.Count;
        var winningTrades = trades.Count(t => /* logic to determine winning trades */ true);

        return new SandboxPerformanceReport
        {
            SandboxSessionId = sandboxSessionId,
            InitialBalance = session.InitialBalance,
            CurrentBalance = session.CurrentBalance + totalMarketValue,
            TotalReturn = (session.CurrentBalance + totalMarketValue) - session.InitialBalance,
            TotalReturnPercent = (((session.CurrentBalance + totalMarketValue) - session.InitialBalance) / session.InitialBalance) * 100,
            RealizedPnL = realizedPnL,
            UnrealizedPnL = totalUnrealizedPnL,
            TotalTrades = totalTrades,
            WinningTrades = winningTrades,
            LosingTrades = totalTrades - winningTrades,
            WinRate = totalTrades > 0 ? (decimal)winningTrades / totalTrades * 100 : 0
        };
    }

    public async Task<bool> LoadHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        // This would typically load data from an external source
        // For now, we'll generate mock historical data
        _logger.LogInformation("Loading historical data for {Symbol} from {StartDate} to {EndDate}",
            symbol, startDate, endDate);

        return true;
    }

    public async Task<int> GetAvailableHistoricalDaysAsync(string symbol)
    {
        var count = await _context.StockQuotes
            .Where(q => q.Symbol == symbol)
            .Select(q => q.Timestamp.Date)
            .Distinct()
            .CountAsync();

        return count;
    }

    public async Task CleanupOldHistoricalDataAsync(int retentionDays = 30)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

        var oldQuotes = await _context.StockQuotes
            .Where(q => q.Timestamp < cutoffDate)
            .ToListAsync();

        _context.StockQuotes.RemoveRange(oldQuotes);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Cleaned up {Count} old historical quotes older than {CutoffDate}",
            oldQuotes.Count, cutoffDate);
    }

    // Helper methods
    private static SandboxSession MapToSandboxSession(SandboxSessionEntity entity)
    {
        return new SandboxSession
        {
            SandboxSessionId = entity.SandboxSessionId,
            UserId = entity.UserId,
            SessionName = entity.SessionName,
            StartDate = entity.StartDate,
            EndDate = entity.EndDate,
            CurrentTime = entity.CurrentTime,
            InitialBalance = entity.InitialBalance,
            CurrentBalance = entity.CurrentBalance,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            CompletedAt = entity.CompletedAt,
            WatchedSymbols = entity.WatchedSymbols?.Split(',').ToList() ?? new List<string>(),
            Settings = new SandboxSettings
            {
                AutoAdvanceTime = entity.Settings?.AutoAdvanceTime ?? true,
                TimeAdvanceIntervalMinutes = entity.Settings?.TimeAdvanceIntervalMinutes ?? 1,
                SkipWeekends = entity.Settings?.SkipWeekends ?? true,
                SkipHolidays = entity.Settings?.SkipHolidays ?? true,
                EnableSlippage = entity.Settings?.EnableSlippage ?? true,
                SlippagePercentage = entity.Settings?.SlippagePercentage ?? 0.1m,
                EnableCommissions = entity.Settings?.EnableCommissions ?? false,
                CommissionPerTrade = entity.Settings?.CommissionPerTrade ?? 0m,
                MaxPositionsPerSymbol = entity.Settings?.MaxPositionsPerSymbol ?? 10000
            }
        };
    }

    private static StockQuote MapToStockQuote(StockQuoteEntity entity)
    {
        return new StockQuote
        {
            Symbol = entity.Symbol,
            LastPrice = entity.LastPrice,
            Change = entity.Change,
            ChangePercent = entity.ChangePercent,
            Volume = entity.Volume
        };
    }

    private StockQuote GenerateMockQuote(string symbol, DateTime timestamp)
    {
        // Generate realistic mock data based on symbol
        var basePrice = symbol switch
        {
            "AAPL" => 175m,
            "TSLA" => 245m,
            "NVDA" => 430m,
            "MSFT" => 375m,
            "GOOGL" => 142m,
            _ => 100m
        };

        var change = (decimal)(_random.NextDouble() - 0.5) * 10; // ±$5 change
        var price = basePrice + change;

        return new StockQuote
        {
            Symbol = symbol,
            LastPrice = price,
            Change = change,
            ChangePercent = (change / basePrice) * 100,
            Volume = _random.Next(1000000, 50000000)
        };
    }

    private static decimal CalculateExecutionPrice(decimal marketPrice, TradeAction action, OrderType orderType, decimal? limitPrice, SandboxSettingsEntity settings)
    {
        var price = marketPrice;

        // Apply slippage if enabled
        if (settings.EnableSlippage)
        {
            var slippageAmount = marketPrice * (settings.SlippagePercentage / 100);
            if (action == TradeAction.Buy)
            {
                price += slippageAmount; // Buy at higher price
            }
            else
            {
                price -= slippageAmount; // Sell at lower price
            }
        }

        // Handle limit orders
        if (orderType == OrderType.Limit && limitPrice.HasValue)
        {
            if (action == TradeAction.Buy && price > limitPrice.Value)
            {
                price = limitPrice.Value;
            }
            else if (action == TradeAction.Sell && price < limitPrice.Value)
            {
                price = limitPrice.Value;
            }
        }

        return Math.Round(price, 2);
    }

    private static MarketSession GetCurrentMarketSession(DateTime currentTime, MarketHours marketHours)
    {
        if (marketHours.IsHoliday)
            return MarketSession.Closed;

        var timeOfDay = currentTime.TimeOfDay;

        if (timeOfDay < marketHours.PreMarketOpen.TimeOfDay)
            return MarketSession.Closed;
        if (timeOfDay < marketHours.MarketOpen.TimeOfDay)
            return MarketSession.PreMarket;
        if (timeOfDay < marketHours.MarketClose.TimeOfDay)
            return MarketSession.Open;
        if (timeOfDay < marketHours.AfterHoursClose.TimeOfDay)
            return MarketSession.AfterHours;

        return MarketSession.Closed;
    }

    private static bool IsMarketHoliday(DateTime date)
    {
        // Simple holiday check - in production, use a comprehensive holiday calendar
        return date.Month == 12 && date.Day == 25; // Christmas
    }

    private static string? GetHolidayName(DateTime date)
    {
        if (date.Month == 12 && date.Day == 25)
            return "Christmas Day";

        return null;
    }

    private static decimal CalculateRealizedPnL(List<SandboxTradeEntity> trades)
    {
        // Simplified P&L calculation - in production, use FIFO/LIFO accounting
        var buyTrades = trades.Where(t => t.Action == TradeAction.Buy).ToList();
        var sellTrades = trades.Where(t => t.Action == TradeAction.Sell).ToList();

        var totalBought = buyTrades.Sum(t => t.TotalValue);
        var totalSold = sellTrades.Sum(t => t.TotalValue);

        return totalSold - totalBought;
    }
}