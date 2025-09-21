using System.ComponentModel.DataAnnotations;
using VBTrader.Core.Models;
using VBTrader.Infrastructure.Database;

namespace VBTrader.Infrastructure.Database.Entities;

public class MarketOpportunityEntity
{
    public long Id { get; set; }

    [Required]
    [MaxLength(10)]
    public string Symbol { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }

    public int OpportunityType { get; set; } // Stored as int, mapped from OpportunityType enum

    public decimal Score { get; set; }
    public decimal VolumeChange { get; set; }
    public decimal PriceChangePercent { get; set; }
    public int NewsSentiment { get; set; } // Stored as int, mapped from NewsRating enum
    public decimal Confidence { get; set; }

    [MaxLength(1000)]
    public string? Reason { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Convert to domain model
    public MarketOpportunity ToDomainModel()
    {
        return new MarketOpportunity
        {
            Symbol = Symbol,
            Timestamp = Timestamp,
            OpportunityType = (OpportunityType)OpportunityType,
            Score = Score,
            VolumeChange = VolumeChange,
            PriceChangePercent = PriceChangePercent,
            NewsSentiment = (NewsRating)NewsSentiment,
            Confidence = Confidence,
            Reason = Reason
        };
    }

    // Create from domain model
    public static MarketOpportunityEntity FromDomainModel(MarketOpportunity opportunity)
    {
        return new MarketOpportunityEntity
        {
            Symbol = opportunity.Symbol,
            Timestamp = opportunity.Timestamp,
            OpportunityType = (int)opportunity.OpportunityType,
            Score = opportunity.Score,
            VolumeChange = opportunity.VolumeChange,
            PriceChangePercent = opportunity.PriceChangePercent,
            NewsSentiment = (int)opportunity.NewsSentiment,
            Confidence = opportunity.Confidence,
            Reason = opportunity.Reason
        };
    }
}