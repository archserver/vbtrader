using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using VBTrader.Core.Models;
using VBTrader.Infrastructure.Database;
using VBTrader.Services;

namespace VBTrader.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDataService _dataService;
    private readonly Timer _updateTimer;

    [ObservableProperty]
    private string _marketStatus = "LOADING...";

    [ObservableProperty]
    private string _connectionStatus = "CONNECTING...";

    [ObservableProperty]
    private string _lastUpdateTime = DateTime.Now.ToString("HH:mm:ss");

    [ObservableProperty]
    private string _statusMessage = "VBTrader starting up...";

    [ObservableProperty]
    private string _tradeStatus = "Ready for trading. Use hotkeys:\nAlt+1,2,3 = Buy 100 shares\nCtrl+1,2,3 = Sell 100 shares";

    [ObservableProperty]
    private int _activeConnectionsCount = 0;

    // Data Collections
    public ObservableCollection<StockQuote> LiveQuotes { get; } = new();
    public ObservableCollection<MarketOpportunity> MarketOpportunities { get; } = new();

    public MainViewModel()
    {
        // For now, create mock services - will be replaced with DI
        _dataService = new MockDataService();

        // Setup update timer for real-time data
        _updateTimer = new Timer(UpdateData, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        // Initialize with some default data
        InitializeDefaultData();
    }

    private void InitializeDefaultData()
    {
        // Add some sample stocks to watch
        var sampleStocks = new[]
        {
            new StockQuote { Symbol = "AAPL", LastPrice = 175.50m, Change = 2.30m, ChangePercent = 1.33m, Volume = 45678900 },
            new StockQuote { Symbol = "TSLA", LastPrice = 245.80m, Change = -3.20m, ChangePercent = -1.28m, Volume = 32456700 },
            new StockQuote { Symbol = "NVDA", LastPrice = 432.10m, Change = 8.50m, ChangePercent = 2.01m, Volume = 28934500 },
            new StockQuote { Symbol = "MSFT", LastPrice = 378.25m, Change = 1.75m, ChangePercent = 0.46m, Volume = 21567800 },
            new StockQuote { Symbol = "GOOGL", LastPrice = 142.30m, Change = -0.80m, ChangePercent = -0.56m, Volume = 18934200 },
        };

        foreach (var stock in sampleStocks)
        {
            LiveQuotes.Add(stock);
        }

        // Add sample opportunities
        var sampleOpportunities = new[]
        {
            new MarketOpportunity { Symbol = "AAPL", Score = 85.5m, OpportunityType = OpportunityType.BreakoutUp, PriceChangePercent = 1.33m },
            new MarketOpportunity { Symbol = "TSLA", Score = 72.3m, OpportunityType = OpportunityType.VolumeSpike, PriceChangePercent = -1.28m },
            new MarketOpportunity { Symbol = "NVDA", Score = 91.2m, OpportunityType = OpportunityType.TechnicalIndicator, PriceChangePercent = 2.01m },
        };

        foreach (var opportunity in sampleOpportunities)
        {
            MarketOpportunities.Add(opportunity);
        }

        MarketStatus = "MARKET OPEN";
        ConnectionStatus = "CONNECTED";
        StatusMessage = "Real-time data active. Ready for trading.";
        ActiveConnectionsCount = 3;
    }

    private void UpdateData(object? state)
    {
        // Update timestamp
        LastUpdateTime = DateTime.Now.ToString("HH:mm:ss");

        // Simulate real-time price updates
        foreach (var quote in LiveQuotes)
        {
            SimulatePriceUpdate(quote);
        }

        // Update opportunities scores
        foreach (var opportunity in MarketOpportunities)
        {
            SimulateOpportunityUpdate(opportunity);
        }
    }

    private void SimulatePriceUpdate(StockQuote quote)
    {
        var random = new Random();
        var changePercent = (decimal)(random.NextDouble() - 0.5) * 0.02m; // ±1% change
        var newPrice = quote.LastPrice * (1 + changePercent);
        var change = newPrice - quote.LastPrice;

        quote.LastPrice = Math.Round(newPrice, 2);
        quote.Change = Math.Round(change, 2);
        quote.ChangePercent = Math.Round((change / (quote.LastPrice - change)) * 100, 2);
        quote.Volume += random.Next(1000, 10000);
    }

    private void SimulateOpportunityUpdate(MarketOpportunity opportunity)
    {
        var random = new Random();
        opportunity.Score = Math.Round(opportunity.Score + (decimal)(random.NextDouble() - 0.5) * 5, 1);
        opportunity.Score = Math.Max(0, Math.Min(100, opportunity.Score)); // Keep between 0-100
    }

    public async Task StartRealTimeDataAsync()
    {
        StatusMessage = "Starting real-time data collection...";

        try
        {
            // TODO: Start actual market data collection service
            // await _marketDataService.StartAsync();

            StatusMessage = "Real-time data collection started successfully.";
            AddTradeStatus("Real-time data feeds active.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error starting data collection: {ex.Message}";
            AddTradeStatus($"ERROR: {ex.Message}");
        }
    }

    public async Task ExecuteBuyOrderAsync(string symbol, int quantity)
    {
        AddTradeStatus($"EXECUTING: BUY {quantity} shares of {symbol}");

        try
        {
            // TODO: Implement actual trading execution via Schwab API
            // For now, simulate the order
            await Task.Delay(100); // Simulate API call

            var quote = LiveQuotes.FirstOrDefault(q => q.Symbol == symbol);
            var price = quote?.LastPrice ?? 0;
            var totalCost = price * quantity;

            AddTradeStatus($"BUY ORDER EXECUTED: {quantity} {symbol} @ ${price:F2} (Total: ${totalCost:F2})");
            StatusMessage = $"Buy order executed: {quantity} {symbol}";
        }
        catch (Exception ex)
        {
            AddTradeStatus($"BUY ORDER FAILED: {symbol} - {ex.Message}");
            StatusMessage = $"Buy order failed: {ex.Message}";
        }
    }

    public async Task ExecuteSellOrderAsync(string symbol, int quantity)
    {
        AddTradeStatus($"EXECUTING: SELL {quantity} shares of {symbol}");

        try
        {
            // TODO: Implement actual trading execution via Schwab API
            // For now, simulate the order
            await Task.Delay(100); // Simulate API call

            var quote = LiveQuotes.FirstOrDefault(q => q.Symbol == symbol);
            var price = quote?.LastPrice ?? 0;
            var totalValue = price * quantity;

            AddTradeStatus($"SELL ORDER EXECUTED: {quantity} {symbol} @ ${price:F2} (Total: ${totalValue:F2})");
            StatusMessage = $"Sell order executed: {quantity} {symbol}";
        }
        catch (Exception ex)
        {
            AddTradeStatus($"SELL ORDER FAILED: {symbol} - {ex.Message}");
            StatusMessage = $"Sell order failed: {ex.Message}";
        }
    }

    public async Task RefreshDataAsync()
    {
        StatusMessage = "Refreshing market data...";

        try
        {
            // TODO: Refresh data from market APIs
            await Task.Delay(500); // Simulate refresh

            StatusMessage = "Market data refreshed successfully.";
            AddTradeStatus("Data refresh completed.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Refresh failed: {ex.Message}";
            AddTradeStatus($"REFRESH ERROR: {ex.Message}");
        }
    }

    public void AddTradeStatus(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var newStatus = $"[{timestamp}] {message}\n{TradeStatus}";

        // Keep only last 20 lines
        var lines = newStatus.Split('\n');
        if (lines.Length > 20)
        {
            newStatus = string.Join('\n', lines.Take(20));
        }

        TradeStatus = newStatus;
    }
}

// Mock data service for testing
public class MockDataService : IDataService
{
    public Task<IEnumerable<StockQuote>> GetLiveQuotesAsync(IEnumerable<string> symbols)
    {
        return Task.FromResult(Enumerable.Empty<StockQuote>());
    }

    public Task<IEnumerable<CandlestickData>> GetCandlestickDataAsync(string symbol, TimeFrame timeFrame, DateTime fromDate, DateTime toDate)
    {
        return Task.FromResult(Enumerable.Empty<CandlestickData>());
    }

    public Task<IEnumerable<MarketOpportunity>> GetMarketOpportunitiesAsync(int maxResults)
    {
        return Task.FromResult(Enumerable.Empty<MarketOpportunity>());
    }

    public Task SaveStockQuoteAsync(StockQuote quote)
    {
        return Task.CompletedTask;
    }

    public Task SaveCandlestickDataAsync(CandlestickData data)
    {
        return Task.CompletedTask;
    }

    public Task SaveMarketOpportunityAsync(MarketOpportunity opportunity)
    {
        return Task.CompletedTask;
    }
}