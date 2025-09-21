namespace VBTrader.Core.Models;

public class MarketSettings
{
    // Market Categories
    public bool EnableSmallCap { get; set; } = true;
    public bool EnableMidCap { get; set; } = true;
    public bool EnableLargeCap { get; set; } = true;
    public bool EnableOptions { get; set; } = false;
    public bool EnableForex { get; set; } = false;
    public bool EnableCrypto { get; set; } = false;
    public bool EnableETFs { get; set; } = true;

    // Price Filters
    public decimal MinPrice { get; set; } = 1.00m;
    public decimal MaxPrice { get; set; } = 10000.00m;

    // Volume Requirements
    public long MinVolume { get; set; } = 100000;
    public long MinPreMarketVolume { get; set; } = 50000;

    // Float Requirements
    public float MinFloat { get; set; } = 1000000; // 1M shares
    public float MaxFloat { get; set; } = 1000000000; // 1B shares

    // Performance Filters
    public decimal MinPreMarketIncrease { get; set; } = 5.0m; // 5% minimum
    public decimal MaxPreMarketIncrease { get; set; } = 1000.0m; // 1000% maximum

    // Market Cap Ranges (in millions)
    public decimal SmallCapMin { get; set; } = 300;      // $300M
    public decimal SmallCapMax { get; set; } = 2000;     // $2B
    public decimal MidCapMin { get; set; } = 2000;       // $2B
    public decimal MidCapMax { get; set; } = 10000;      // $10B
    public decimal LargeCapMin { get; set; } = 10000;    // $10B+

    // Data Collection Frequencies
    public int PreMarketUpdateIntervalMs { get; set; } = 200;  // 5x per second
    public int MarketHoursUpdateIntervalMs { get; set; } = 50; // 20x per second
    public int OpportunityScaniIntervalMs { get; set; } = 60000; // Every minute
    public int TickerDiscoveryIntervalMs { get; set; } = 3600000; // Every hour

    // Data Retention
    public int DataRetentionDays { get; set; } = 7; // Default 1 week
    public int MaxDataRetentionDays { get; set; } = 547; // 18 months max
}