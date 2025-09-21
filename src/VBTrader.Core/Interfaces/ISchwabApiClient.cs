using VBTrader.Core.Models;

namespace VBTrader.Core.Interfaces;

public interface ISchwabApiClient
{
    // Authentication
    Task<bool> AuthenticateAsync(string appKey, string appSecret, string callbackUrl);
    Task<bool> RefreshTokenAsync();
    bool IsAuthenticated { get; }

    // Account Management
    Task<IEnumerable<AccountInfo>> GetLinkedAccountsAsync();
    Task<AccountDetails> GetAccountDetailsAsync(string accountHash, bool includePositions = false);
    Task<IEnumerable<Order>> GetOrdersAsync(string accountHash, DateTime fromDate, DateTime toDate);

    // Market Data
    Task<StockQuote> GetQuoteAsync(string symbol);
    Task<IEnumerable<StockQuote>> GetQuotesAsync(IEnumerable<string> symbols);
    Task<IEnumerable<CandlestickData>> GetPriceHistoryAsync(string symbol, TimeFrame timeFrame, DateTime? startDate = null, DateTime? endDate = null);
    Task<IEnumerable<CandlestickData>> GetPriceHistoryAsync(PriceHistoryRequest request);
    Task<Dictionary<string, IEnumerable<CandlestickData>>> GetBulkPriceHistoryAsync(BulkPriceHistoryRequest request);
    Task<IEnumerable<StockQuote>> GetMoversAsync(string index, MoversSort sort, int? frequency = null);

    // Options
    Task<OptionChain> GetOptionChainAsync(string symbol, string? contractType = null);

    // Trading
    Task<OrderResult> PlaceOrderAsync(string accountHash, OrderRequest order);
    Task<bool> CancelOrderAsync(string accountHash, string orderId);
    Task<Order> GetOrderDetailsAsync(string accountHash, string orderId);

    // Streaming
    Task StartStreamingAsync(IEnumerable<string> symbols, Action<StreamingData> onDataReceived);
    Task StopStreamingAsync();
    bool IsStreaming { get; }

    // Account tracking
    void SetCurrentUserId(int userId);
}

public class AccountInfo
{
    public string AccountNumber { get; set; } = string.Empty;
    public string HashValue { get; set; } = string.Empty;
}

public class AccountDetails
{
    public string AccountNumber { get; set; } = string.Empty;
    public decimal TotalValue { get; set; }
    public decimal CashBalance { get; set; }
    public decimal BuyingPower { get; set; }
    public IEnumerable<Position> Positions { get; set; } = new List<Position>();
}

public class Position
{
    public string Symbol { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal AveragePrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal UnrealizedPnL { get; set; }
}

public class Order
{
    public string OrderId { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public OrderType OrderType { get; set; }
    public OrderSide Side { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime CreatedTime { get; set; }
}

public class OrderRequest
{
    public string Symbol { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal? Price { get; set; }
    public OrderType OrderType { get; set; }
    public OrderSide Side { get; set; }
    public string Duration { get; set; } = "DAY";
}

public class OrderResult
{
    public bool Success { get; set; }
    public string? OrderId { get; set; }
    public string? ErrorMessage { get; set; }
}

public class OptionChain
{
    public string Symbol { get; set; } = string.Empty;
    public Dictionary<DateTime, OptionExpiration> Expirations { get; set; } = new();
}

public class OptionExpiration
{
    public DateTime ExpirationDate { get; set; }
    public Dictionary<decimal, OptionStrike> Strikes { get; set; } = new();
}

public class OptionStrike
{
    public decimal StrikePrice { get; set; }
    public OptionContract? Call { get; set; }
    public OptionContract? Put { get; set; }
}

public class OptionContract
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public decimal LastPrice { get; set; }
    public int Volume { get; set; }
    public int OpenInterest { get; set; }
}

public class StreamingData
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public long Volume { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum OrderType
{
    Market,
    Limit,
    Stop,
    StopLimit
}

public enum OrderSide
{
    Buy,
    Sell
}

public enum OrderStatus
{
    Pending,
    Filled,
    PartiallyFilled,
    Cancelled,
    Rejected
}

public enum MoversSort
{
    Volume,
    Trades,
    PercentChangeUp,
    PercentChangeDown
}