using System.ComponentModel.DataAnnotations;

namespace VBTrader.Core.Models;

public class MarketOpportunity
{
    [Required]
    public string Symbol { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }
    public OpportunityType OpportunityType { get; set; }
    public decimal Score { get; set; }
    public decimal VolumeChange { get; set; }
    public decimal PriceChangePercent { get; set; }
    public NewsRating NewsSentiment { get; set; }
    public decimal Confidence { get; set; }
    public string? Reason { get; set; }
}

public enum OpportunityType
{
    None = 0,
    BreakoutUp = 1,
    BreakoutDown = 2,
    VolumeSpike = 3,
    NewsEvent = 4,
    TechnicalIndicator = 5,
    PreMarketMover = 6,
    PostMarketMover = 7,
    EarningsMove = 8,
    AnalystUpgrade = 9,
    AnalystDowngrade = 10,
    SectorRotation = 11,
    UnusualOptions = 12
}