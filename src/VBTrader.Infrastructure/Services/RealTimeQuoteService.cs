using Microsoft.Extensions.Logging;
using VBTrader.Core.Interfaces;
using VBTrader.Core.Models;

namespace VBTrader.Infrastructure.Services;

public class RealTimeQuoteService : IDisposable
{
    private readonly ISchwabApiClient _schwabApiClient;
    private readonly ILogger<RealTimeQuoteService> _logger;
    private readonly Timer? _updateTimer;
    private readonly object _lockObject = new();

    private bool _isRunning;
    private bool _disposed;
    private List<string> _watchedSymbols = new();
    private Dictionary<string, StockQuote> _currentQuotes = new();
    private Dictionary<string, StockQuote> _mockQuotes = new();

    // Events for quote updates
    public event EventHandler<QuoteUpdateEventArgs>? QuoteUpdated;
    public event EventHandler<BulkQuoteUpdateEventArgs>? BulkQuotesUpdated;

    // Configuration
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(5); // 5 second updates
    public bool UseMockData { get; set; } = true; // Default to mock data until Schwab is connected
    public int MaxSymbols { get; set; } = 10;

    public RealTimeQuoteService(ISchwabApiClient schwabApiClient, ILogger<RealTimeQuoteService> logger)
    {
        _schwabApiClient = schwabApiClient;
        _logger = logger;

        // Initialize with common symbols
        _watchedSymbols = new List<string> { "AAPL", "MSFT", "GOOGL", "AMZN", "TSLA" };

        InitializeMockQuotes();

        // Create timer for periodic updates
        _updateTimer = new Timer(UpdateQuotesCallback, null, Timeout.Infinite, Timeout.Infinite);

        _logger.LogInformation("RealTimeQuoteService initialized with {Count} symbols", _watchedSymbols.Count);
    }

    public void Start()
    {
        lock (_lockObject)
        {
            if (_isRunning || _disposed) return;

            _isRunning = true;
            _updateTimer?.Change(TimeSpan.Zero, UpdateInterval);
            _logger.LogInformation("RealTimeQuoteService started with {Interval}s update interval", UpdateInterval.TotalSeconds);
        }
    }

    public void Stop()
    {
        lock (_lockObject)
        {
            if (!_isRunning) return;

            _isRunning = false;
            _updateTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _logger.LogInformation("RealTimeQuoteService stopped");
        }
    }

    public void SetWatchedSymbols(IEnumerable<string> symbols)
    {
        lock (_lockObject)
        {
            var newSymbols = symbols.Take(MaxSymbols).ToList();

            if (_watchedSymbols.SequenceEqual(newSymbols))
                return;

            _watchedSymbols = newSymbols;
            _currentQuotes.Clear();

            // Update mock data for new symbols
            InitializeMockQuotes();

            _logger.LogInformation("Updated watched symbols: {Symbols}", string.Join(", ", _watchedSymbols));
        }
    }

    public void AddSymbol(string symbol)
    {
        lock (_lockObject)
        {
            symbol = symbol.ToUpper().Trim();

            if (_watchedSymbols.Contains(symbol) || _watchedSymbols.Count >= MaxSymbols)
                return;

            _watchedSymbols.Add(symbol);
            AddMockQuote(symbol);

            _logger.LogInformation("Added symbol to watchlist: {Symbol}", symbol);
        }
    }

    public void RemoveSymbol(string symbol)
    {
        lock (_lockObject)
        {
            symbol = symbol.ToUpper().Trim();

            if (!_watchedSymbols.Remove(symbol))
                return;

            _currentQuotes.Remove(symbol);
            _mockQuotes.Remove(symbol);

            _logger.LogInformation("Removed symbol from watchlist: {Symbol}", symbol);
        }
    }

    public IEnumerable<StockQuote> GetCurrentQuotes()
    {
        lock (_lockObject)
        {
            return UseMockData ? _mockQuotes.Values.ToList() : _currentQuotes.Values.ToList();
        }
    }

    public StockQuote? GetQuote(string symbol)
    {
        lock (_lockObject)
        {
            symbol = symbol.ToUpper().Trim();
            var quotes = UseMockData ? _mockQuotes : _currentQuotes;
            return quotes.TryGetValue(symbol, out var quote) ? quote : null;
        }
    }

    public void SetSchwabConnectionStatus(bool isConnected)
    {
        if (UseMockData == !isConnected) return;

        UseMockData = !isConnected;
        _logger.LogInformation("Switched to {DataSource} data", UseMockData ? "mock" : "live Schwab");

        if (!UseMockData)
        {
            // Clear current quotes when switching to live data
            lock (_lockObject)
            {
                _currentQuotes.Clear();
            }
        }
    }

    private async void UpdateQuotesCallback(object? state)
    {
        if (!_isRunning || _disposed) return;

        try
        {
            List<string> symbolsToUpdate;
            lock (_lockObject)
            {
                symbolsToUpdate = _watchedSymbols.ToList();
            }

            if (!symbolsToUpdate.Any()) return;

            if (UseMockData)
            {
                UpdateMockQuotes();
            }
            else
            {
                await UpdateLiveQuotes(symbolsToUpdate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating quotes");
        }
    }

    private async Task UpdateLiveQuotes(List<string> symbols)
    {
        try
        {
            if (!_schwabApiClient.IsAuthenticated)
            {
                _logger.LogWarning("Schwab API not authenticated, switching to mock data");
                SetSchwabConnectionStatus(false);
                return;
            }

            var quotes = await _schwabApiClient.GetQuotesAsync(symbols);
            var quoteList = quotes.ToList();

            if (!quoteList.Any())
            {
                _logger.LogWarning("No quotes received from Schwab API");
                return;
            }

            lock (_lockObject)
            {
                var updatedQuotes = new List<StockQuote>();

                foreach (var quote in quoteList)
                {
                    var previousQuote = _currentQuotes.TryGetValue(quote.Symbol, out var prev) ? prev : null;
                    _currentQuotes[quote.Symbol] = quote;
                    updatedQuotes.Add(quote);

                    // Fire individual quote update event
                    QuoteUpdated?.Invoke(this, new QuoteUpdateEventArgs(quote, previousQuote));
                }

                // Fire bulk update event
                BulkQuotesUpdated?.Invoke(this, new BulkQuoteUpdateEventArgs(updatedQuotes));
            }

            _logger.LogDebug("Updated {Count} live quotes", quoteList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update live quotes");

            // Fall back to mock data on error
            SetSchwabConnectionStatus(false);
        }
    }

    private void UpdateMockQuotes()
    {
        var random = new Random();
        var updatedQuotes = new List<StockQuote>();

        lock (_lockObject)
        {
            foreach (var symbol in _watchedSymbols)
            {
                if (!_mockQuotes.TryGetValue(symbol, out var quote)) continue;

                var previousQuote = new StockQuote
                {
                    Symbol = quote.Symbol,
                    LastPrice = quote.LastPrice,
                    Change = quote.Change,
                    ChangePercent = quote.ChangePercent,
                    Volume = quote.Volume
                };

                // Simulate realistic price movements (±2% max change)
                var changePercent = (decimal)(random.NextDouble() - 0.5) * 0.04m; // ±2%
                var newPrice = quote.LastPrice * (1 + changePercent);
                var change = newPrice - quote.LastPrice;

                quote.LastPrice = Math.Round(newPrice, 2);
                quote.Change = Math.Round(change, 2);
                quote.ChangePercent = Math.Round((change / (quote.LastPrice - change)) * 100, 2);
                quote.Volume += random.Next(1000, 50000);

                updatedQuotes.Add(quote);

                // Fire individual quote update event
                QuoteUpdated?.Invoke(this, new QuoteUpdateEventArgs(quote, previousQuote));
            }

            // Fire bulk update event
            if (updatedQuotes.Any())
            {
                BulkQuotesUpdated?.Invoke(this, new BulkQuoteUpdateEventArgs(updatedQuotes));
            }
        }

        _logger.LogDebug("Updated {Count} mock quotes", updatedQuotes.Count);
    }

    private void InitializeMockQuotes()
    {
        var random = new Random();
        var basePrices = new Dictionary<string, decimal>
        {
            { "AAPL", 175.50m },
            { "MSFT", 378.25m },
            { "GOOGL", 142.30m },
            { "AMZN", 145.80m },
            { "TSLA", 245.80m },
            { "NVDA", 432.10m },
            { "META", 325.60m },
            { "NFLX", 485.20m },
            { "CRM", 220.15m },
            { "ORCL", 115.75m }
        };

        _mockQuotes.Clear();

        foreach (var symbol in _watchedSymbols)
        {
            AddMockQuote(symbol, basePrices.GetValueOrDefault(symbol, 100m + (decimal)(random.NextDouble() * 400)));
        }
    }

    private void AddMockQuote(string symbol, decimal? basePrice = null)
    {
        var random = new Random();
        var price = basePrice ?? (100m + (decimal)(random.NextDouble() * 400));
        var change = (decimal)(random.NextDouble() - 0.5) * 10;

        _mockQuotes[symbol] = new StockQuote
        {
            Symbol = symbol,
            LastPrice = Math.Round(price, 2),
            Change = Math.Round(change, 2),
            ChangePercent = Math.Round((change / price) * 100, 2),
            Volume = random.Next(1000000, 50000000)
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _updateTimer?.Dispose();
        _disposed = true;

        _logger.LogInformation("RealTimeQuoteService disposed");
    }
}

// Event argument classes
public class QuoteUpdateEventArgs : EventArgs
{
    public StockQuote CurrentQuote { get; }
    public StockQuote? PreviousQuote { get; }

    public QuoteUpdateEventArgs(StockQuote currentQuote, StockQuote? previousQuote = null)
    {
        CurrentQuote = currentQuote;
        PreviousQuote = previousQuote;
    }
}

public class BulkQuoteUpdateEventArgs : EventArgs
{
    public IReadOnlyList<StockQuote> UpdatedQuotes { get; }

    public BulkQuoteUpdateEventArgs(IEnumerable<StockQuote> updatedQuotes)
    {
        UpdatedQuotes = updatedQuotes.ToList().AsReadOnly();
    }
}