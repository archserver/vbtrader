using Microsoft.Extensions.Logging;
using VBTrader.Core.Interfaces;
using VBTrader.Core.Models;

namespace VBTrader.Infrastructure.Services;

public class SandboxTradingService : IDisposable
{
    private readonly IHistoricalDataService _historicalDataService;
    private readonly ISandboxDataService _sandboxDataService;
    private readonly ILogger<SandboxTradingService> _logger;

    private readonly object _lockObject = new();
    private Timer? _replayTimer;
    private bool _disposed;

    // Replay state
    private bool _isReplaying;
    private DateTime _currentReplayTime;
    private DateTime _replayStartTime;
    private DateTime _replayEndTime;
    private TimeSpan _replaySpeed = TimeSpan.FromSeconds(1); // Real-time by default
    private List<string> _replaySymbols = new();
    private int _currentSandboxSessionId;

    // Current market state
    private Dictionary<string, StockQuote> _currentMarketState = new();
    private List<SandboxTrade> _executedTrades = new();

    // Events
    public event EventHandler<MarketDataReplayEventArgs>? MarketDataUpdated;
    public event EventHandler<TradeExecutionEventArgs>? TradeExecuted;
    public event EventHandler<ReplayStatusEventArgs>? ReplayStatusChanged;

    // Configuration
    public bool IsReplaying => _isReplaying;
    public DateTime CurrentReplayTime => _currentReplayTime;
    public TimeSpan ReplaySpeed => _replaySpeed;
    public double ReplayProgress => _replayStartTime == _replayEndTime ? 0 :
        (_currentReplayTime - _replayStartTime).TotalMilliseconds / (_replayEndTime - _replayStartTime).TotalMilliseconds;

    public SandboxTradingService(
        IHistoricalDataService historicalDataService,
        ISandboxDataService sandboxDataService,
        ILogger<SandboxTradingService> logger)
    {
        _historicalDataService = historicalDataService;
        _sandboxDataService = sandboxDataService;
        _logger = logger;

        _logger.LogInformation("SandboxTradingService initialized");
    }

    public async Task<bool> StartReplayAsync(
        int sandboxSessionId,
        List<string> symbols,
        DateTime startTime,
        DateTime endTime,
        TimeSpan? replaySpeed = null)
    {
        lock (_lockObject)
        {
            if (_isReplaying)
            {
                _logger.LogWarning("Replay already in progress");
                return false;
            }

            _currentSandboxSessionId = sandboxSessionId;
            _replaySymbols = symbols.ToList();
            _replayStartTime = startTime;
            _replayEndTime = endTime;
            _currentReplayTime = startTime;
            _replaySpeed = replaySpeed ?? TimeSpan.FromSeconds(1);

            _currentMarketState.Clear();
            _executedTrades.Clear();

            _isReplaying = true;
        }

        try
        {
            // Validate that we have historical data for the replay period
            var hasData = await ValidateHistoricalDataAvailability();
            if (!hasData)
            {
                _logger.LogError("Insufficient historical data for replay period");
                StopReplay();
                return false;
            }

            // Initialize market state at start time
            await InitializeMarketState();

            // Start the replay timer
            _replayTimer = new Timer(ReplayTickCallback, null, TimeSpan.Zero, _replaySpeed);

            _logger.LogInformation("Started sandbox replay: {Symbols} from {Start} to {End} at {Speed}x speed",
                string.Join(", ", _replaySymbols), _replayStartTime, _replayEndTime, _replaySpeed.TotalSeconds);

            ReplayStatusChanged?.Invoke(this, new ReplayStatusEventArgs(true, _currentReplayTime, ReplayProgress));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start replay");
            StopReplay();
            return false;
        }
    }

    public void StopReplay()
    {
        lock (_lockObject)
        {
            if (!_isReplaying) return;

            _isReplaying = false;
            _replayTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        _logger.LogInformation("Stopped sandbox replay");
        ReplayStatusChanged?.Invoke(this, new ReplayStatusEventArgs(false, _currentReplayTime, ReplayProgress));
    }

    public void PauseReplay()
    {
        lock (_lockObject)
        {
            if (!_isReplaying) return;
            _replayTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        _logger.LogInformation("Paused sandbox replay");
    }

    public void ResumeReplay()
    {
        lock (_lockObject)
        {
            if (!_isReplaying) return;
            _replayTimer?.Change(TimeSpan.Zero, _replaySpeed);
        }

        _logger.LogInformation("Resumed sandbox replay");
    }

    public void SetReplaySpeed(TimeSpan speed)
    {
        lock (_lockObject)
        {
            _replaySpeed = speed;
            if (_isReplaying)
            {
                _replayTimer?.Change(TimeSpan.Zero, _replaySpeed);
            }
        }

        _logger.LogInformation("Changed replay speed to {Speed}x", speed.TotalSeconds);
    }

    public async Task<SandboxTradeResult> ExecuteTradeAsync(
        string symbol,
        TradeAction action,
        int quantity,
        OrderType orderType = OrderType.Market,
        decimal? limitPrice = null)
    {
        if (!_isReplaying)
        {
            return new SandboxTradeResult
            {
                Success = false,
                ErrorMessage = "Replay not active"
            };
        }

        if (!_currentMarketState.TryGetValue(symbol, out var currentQuote))
        {
            return new SandboxTradeResult
            {
                Success = false,
                ErrorMessage = $"No market data available for {symbol}"
            };
        }

        try
        {
            // Execute the trade through the sandbox service
            var result = await _sandboxDataService.ExecuteSandboxTradeAsync(
                _currentSandboxSessionId,
                symbol,
                action,
                quantity,
                orderType,
                limitPrice);

            if (result.Success)
            {
                // Create a trade record with replay timestamp
                var trade = new SandboxTrade
                {
                    Symbol = symbol,
                    Action = action,
                    Quantity = quantity,
                    ExecutionPrice = result.ExecutionPrice,
                    TotalCost = result.TotalCost,
                    ExecutionTime = _currentReplayTime,
                    OrderType = orderType
                };

                _executedTrades.Add(trade);

                _logger.LogInformation("Executed sandbox trade: {Action} {Quantity} {Symbol} @ ${Price} at {Time}",
                    action, quantity, symbol, result.ExecutionPrice, _currentReplayTime);

                TradeExecuted?.Invoke(this, new TradeExecutionEventArgs(trade, result));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing sandbox trade");
            return new SandboxTradeResult
            {
                Success = false,
                ErrorMessage = $"Trade execution failed: {ex.Message}"
            };
        }
    }

    public IEnumerable<StockQuote> GetCurrentMarketState()
    {
        lock (_lockObject)
        {
            return _currentMarketState.Values.ToList();
        }
    }

    public StockQuote? GetCurrentQuote(string symbol)
    {
        lock (_lockObject)
        {
            return _currentMarketState.TryGetValue(symbol, out var quote) ? quote : null;
        }
    }

    public IEnumerable<SandboxTrade> GetExecutedTrades()
    {
        return _executedTrades.ToList();
    }

    public async Task<ReplayPerformanceMetrics> GetPerformanceMetricsAsync()
    {
        // Get current balance from sandbox service
        var currentBalance = await _sandboxDataService.GetSandboxAccountBalanceAsync(_currentSandboxSessionId);

        // For now, assume initial balance is $100,000 - in real implementation we'd store this
        var initialBalance = 100000m;

        var totalTrades = _executedTrades.Count;
        var profitableTrades = _executedTrades.Count(t => t.Action == TradeAction.Sell && t.TotalCost > 0);
        var totalProfit = currentBalance - initialBalance;

        return new ReplayPerformanceMetrics
        {
            StartTime = _replayStartTime,
            CurrentTime = _currentReplayTime,
            EndTime = _replayEndTime,
            Progress = ReplayProgress,
            InitialBalance = initialBalance,
            CurrentBalance = currentBalance,
            TotalProfit = totalProfit,
            TotalTrades = totalTrades,
            ProfitableTrades = profitableTrades,
            WinRate = totalTrades > 0 ? (decimal)profitableTrades / totalTrades : 0,
            ExecutedTrades = _executedTrades.ToList()
        };
    }

    private async Task<bool> ValidateHistoricalDataAvailability()
    {
        try
        {
            foreach (var symbol in _replaySymbols)
            {
                var validation = await _historicalDataService.ValidateDataIntegrityAsync(
                    symbol, TimeFrame.OneMinute, _replayStartTime, _replayEndTime);

                if (!validation.IsValid || validation.TotalRecords == 0)
                {
                    _logger.LogWarning("Insufficient data for {Symbol} in replay period", symbol);
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating historical data availability");
            return false;
        }
    }

    private async Task InitializeMarketState()
    {
        try
        {
            // Get initial quotes at the start time
            var quotes = await _sandboxDataService.GetHistoricalQuotesAsync(_replaySymbols, _currentReplayTime);

            _currentMarketState.Clear();
            foreach (var quote in quotes)
            {
                _currentMarketState[quote.Symbol] = quote;
            }

            _logger.LogDebug("Initialized market state with {Count} symbols at {Time}",
                _currentMarketState.Count, _currentReplayTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing market state");
        }
    }

    private async void ReplayTickCallback(object? state)
    {
        if (!_isReplaying || _disposed) return;

        try
        {
            // Check if replay is complete
            if (_currentReplayTime >= _replayEndTime)
            {
                StopReplay();
                return;
            }

            // Advance time by 1 minute (based on historical data granularity)
            _currentReplayTime = _currentReplayTime.AddMinutes(1);

            // Update market state with historical data
            await UpdateMarketState();

            // Notify listeners of market update
            MarketDataUpdated?.Invoke(this, new MarketDataReplayEventArgs(
                _currentMarketState.Values.ToList(),
                _currentReplayTime,
                ReplayProgress));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during replay tick");
        }
    }

    private async Task UpdateMarketState()
    {
        try
        {
            var quotes = await _sandboxDataService.GetHistoricalQuotesAsync(_replaySymbols, _currentReplayTime);

            foreach (var quote in quotes)
            {
                _currentMarketState[quote.Symbol] = quote;
            }

            _logger.LogDebug("Updated market state with {Count} quotes at {Time}",
                quotes.Count(), _currentReplayTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating market state during replay");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopReplay();
        _replayTimer?.Dispose();
        _disposed = true;

        _logger.LogInformation("SandboxTradingService disposed");
    }
}

// Event argument classes
public class MarketDataReplayEventArgs : EventArgs
{
    public IReadOnlyList<StockQuote> MarketData { get; }
    public DateTime ReplayTime { get; }
    public double Progress { get; }

    public MarketDataReplayEventArgs(IEnumerable<StockQuote> marketData, DateTime replayTime, double progress)
    {
        MarketData = marketData.ToList().AsReadOnly();
        ReplayTime = replayTime;
        Progress = progress;
    }
}

public class TradeExecutionEventArgs : EventArgs
{
    public SandboxTrade Trade { get; }
    public SandboxTradeResult Result { get; }

    public TradeExecutionEventArgs(SandboxTrade trade, SandboxTradeResult result)
    {
        Trade = trade;
        Result = result;
    }
}

public class ReplayStatusEventArgs : EventArgs
{
    public bool IsActive { get; }
    public DateTime CurrentTime { get; }
    public double Progress { get; }

    public ReplayStatusEventArgs(bool isActive, DateTime currentTime, double progress)
    {
        IsActive = isActive;
        CurrentTime = currentTime;
        Progress = progress;
    }
}

// Data models
public class SandboxTrade
{
    public string Symbol { get; set; } = string.Empty;
    public TradeAction Action { get; set; }
    public int Quantity { get; set; }
    public decimal ExecutionPrice { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime ExecutionTime { get; set; }
    public OrderType OrderType { get; set; }
}

public class ReplayPerformanceMetrics
{
    public DateTime StartTime { get; set; }
    public DateTime CurrentTime { get; set; }
    public DateTime EndTime { get; set; }
    public double Progress { get; set; }
    public decimal InitialBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal TotalProfit { get; set; }
    public int TotalTrades { get; set; }
    public int ProfitableTrades { get; set; }
    public decimal WinRate { get; set; }
    public List<SandboxTrade> ExecutedTrades { get; set; } = new();
}