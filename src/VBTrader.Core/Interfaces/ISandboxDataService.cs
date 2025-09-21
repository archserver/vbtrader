using VBTrader.Core.Models;

namespace VBTrader.Core.Interfaces;

public interface ISandboxDataService
{
    // Sandbox Configuration
    Task<SandboxSession> CreateSandboxSessionAsync(int userId, DateTime startDate, DateTime endDate, decimal initialBalance = 100000m);
    Task<SandboxSession?> GetActiveSandboxSessionAsync(int userId);
    Task<bool> EndSandboxSessionAsync(int sandboxSessionId);
    Task<IEnumerable<SandboxSession>> GetSandboxSessionHistoryAsync(int userId);

    // Historical Data Replay
    Task<IEnumerable<StockQuote>> GetHistoricalQuotesAsync(IEnumerable<string> symbols, DateTime timestamp);
    Task<IEnumerable<StockQuote>> GetHistoricalQuotesForPeriodAsync(IEnumerable<string> symbols, DateTime startDate, DateTime endDate);
    Task<bool> HasHistoricalDataAsync(string symbol, DateTime date);
    Task<DateRange> GetAvailableDataRangeAsync(string symbol);

    // Sandbox Time Control
    Task<DateTime> GetSandboxCurrentTimeAsync(int sandboxSessionId);
    Task<bool> SetSandboxTimeAsync(int sandboxSessionId, DateTime sandboxTime);
    Task<bool> AdvanceSandboxTimeAsync(int sandboxSessionId, TimeSpan interval);
    Task<SandboxTimeStatus> GetSandboxTimeStatusAsync(int sandboxSessionId);

    // Market Data Simulation
    Task<IEnumerable<StockQuote>> GetCurrentSandboxQuotesAsync(int sandboxSessionId, IEnumerable<string> symbols);
    Task<IEnumerable<MarketOpportunity>> GetCurrentSandboxOpportunitiesAsync(int sandboxSessionId);
    Task<bool> IsMarketOpenAsync(int sandboxSessionId);
    Task<MarketHours> GetMarketHoursAsync(int sandboxSessionId, DateTime date);

    // Sandbox Trading
    Task<SandboxTradeResult> ExecuteSandboxTradeAsync(int sandboxSessionId, string symbol, TradeAction action, int quantity, OrderType orderType, decimal? limitPrice = null);
    Task<IEnumerable<SandboxPosition>> GetSandboxPositionsAsync(int sandboxSessionId);
    Task<decimal> GetSandboxAccountBalanceAsync(int sandboxSessionId);
    Task<SandboxPerformanceReport> GetSandboxPerformanceAsync(int sandboxSessionId);

    // Data Management
    Task<bool> LoadHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate);
    Task<int> GetAvailableHistoricalDaysAsync(string symbol);
    Task CleanupOldHistoricalDataAsync(int retentionDays = 30);
}

public class SandboxSession
{
    public int SandboxSessionId { get; set; }
    public int UserId { get; set; }
    public string SessionName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime CurrentTime { get; set; }
    public decimal InitialBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<string> WatchedSymbols { get; set; } = new();
    public SandboxSettings Settings { get; set; } = new();
}

public class SandboxSettings
{
    public bool AutoAdvanceTime { get; set; } = true;
    public int TimeAdvanceIntervalMinutes { get; set; } = 1;
    public bool SkipWeekends { get; set; } = true;
    public bool SkipHolidays { get; set; } = true;
    public bool EnableSlippage { get; set; } = true;
    public decimal SlippagePercentage { get; set; } = 0.1m;
    public bool EnableCommissions { get; set; } = false;
    public decimal CommissionPerTrade { get; set; } = 0m;
    public int MaxPositionsPerSymbol { get; set; } = 10000;
}

public class SandboxTimeStatus
{
    public DateTime CurrentTime { get; set; }
    public bool IsMarketOpen { get; set; }
    public MarketSession CurrentSession { get; set; }
    public DateTime NextMarketOpen { get; set; }
    public DateTime NextMarketClose { get; set; }
    public bool CanAdvanceTime { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}

public class SandboxTradeResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int TradeId { get; set; }
    public decimal ExecutionPrice { get; set; }
    public DateTime ExecutionTime { get; set; }
    public decimal Slippage { get; set; }
    public decimal Commission { get; set; }
    public decimal TotalCost { get; set; }
    public decimal NewBalance { get; set; }
}

public class SandboxPosition
{
    public string Symbol { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal AveragePrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal MarketValue { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public decimal UnrealizedPnLPercent { get; set; }
    public DateTime FirstPurchaseDate { get; set; }
    public DateTime LastUpdateDate { get; set; }
}

public class SandboxPerformanceReport
{
    public int SandboxSessionId { get; set; }
    public decimal InitialBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal TotalReturn { get; set; }
    public decimal TotalReturnPercent { get; set; }
    public decimal RealizedPnL { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal LargestWin { get; set; }
    public decimal LargestLoss { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal SharpeRatio { get; set; }
    public List<DailyPerformance> DailyPerformance { get; set; } = new();
    public Dictionary<string, SymbolPerformance> SymbolPerformance { get; set; } = new();
}

public class DailyPerformance
{
    public DateTime Date { get; set; }
    public decimal Balance { get; set; }
    public decimal DailyReturn { get; set; }
    public decimal CumulativeReturn { get; set; }
}

public class SymbolPerformance
{
    public string Symbol { get; set; } = string.Empty;
    public int TotalTrades { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal WinRate { get; set; }
    public decimal AverageReturn { get; set; }
}

public class DateRange
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalDays { get; set; }
    public int TradingDays { get; set; }
}

public class MarketHours
{
    public DateTime Date { get; set; }
    public DateTime PreMarketOpen { get; set; }
    public DateTime MarketOpen { get; set; }
    public DateTime MarketClose { get; set; }
    public DateTime AfterHoursClose { get; set; }
    public bool IsHoliday { get; set; }
    public string? HolidayName { get; set; }
}

public enum MarketSession
{
    Closed,
    PreMarket,
    Open,
    AfterHours
}

