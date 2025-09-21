using System.ComponentModel.DataAnnotations;

namespace VBTrader.Core.Models;

public class StockQuote
{
    [Required]
    public string Symbol { get; set; } = string.Empty;

    public decimal LastPrice { get; set; }
    public decimal Change { get; set; }
    public decimal ChangePercent { get; set; }
    public long Volume { get; set; }
    public decimal Ask { get; set; }
    public decimal Bid { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Open { get; set; }
    public decimal PreviousClose { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public bool IsPreMarket { get; set; }
    public decimal MarketCap { get; set; }
    public float SharesFloat { get; set; }

    // Calculated properties
    public decimal PreMarketChangePercent => PreviousClose != 0 ?
        ((LastPrice - PreviousClose) / PreviousClose) * 100 : 0;

    public bool IsGainer => Change > 0;
    public bool IsLoser => Change < 0;

    // News analysis rating
    public NewsRating NewsRating { get; set; } = NewsRating.None;
    public string? NewsHeadline { get; set; }
    public DateTime? NewsTimestamp { get; set; }
}

public enum NewsRating
{
    None = 0,
    Bad = 1,
    OK = 2,
    Good = 3,
    Great = 4,
    Amazing = 5
}