using System.ComponentModel.DataAnnotations;

namespace VBTrader.Infrastructure.Database.Entities;

public class TradingSessionEntity
{
    public long Id { get; set; }

    public DateTime SessionDate { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    public bool IsPreMarketSession { get; set; }
    public bool IsMarketHoursSession { get; set; }
    public bool IsAfterHoursSession { get; set; }

    // Session statistics
    public int TotalQuotesProcessed { get; set; }
    public int TotalOpportunitiesFound { get; set; }
    public int TotalSymbolsWatched { get; set; }
    public int TotalActiveSymbols { get; set; }

    // Performance metrics
    public decimal AverageResponseTimeMs { get; set; }
    public int TotalApiCalls { get; set; }
    public int FailedApiCalls { get; set; }

    // Data quality metrics
    public decimal DataQualityScore { get; set; }
    public int MissedUpdates { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}