using System.ComponentModel.DataAnnotations;
using VBTrader.Core.Models;

namespace VBTrader.Infrastructure.Database.Entities;

public class StockQuoteEntity
{
    public long Id { get; set; }

    [Required]
    [MaxLength(10)]
    public string Symbol { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }

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
    public decimal MarketCap { get; set; }
    public float SharesFloat { get; set; }
    public bool IsPreMarket { get; set; }
    public decimal PreMarketChangePercent { get; set; }

    // News data
    public int NewsRating { get; set; } // Stored as int, mapped from NewsRating enum

    [MaxLength(500)]
    public string? NewsHeadline { get; set; }

    public DateTime? NewsTimestamp { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Convert to domain model
    public StockQuote ToDomainModel()
    {
        return new StockQuote
        {
            Symbol = Symbol,
            LastPrice = LastPrice,
            Change = Change,
            ChangePercent = ChangePercent,
            Volume = Volume,
            Ask = Ask,
            Bid = Bid,
            High = High,
            Low = Low,
            Open = Open,
            PreviousClose = PreviousClose,
            MarketCap = MarketCap,
            SharesFloat = SharesFloat,
            IsPreMarket = IsPreMarket,
            LastUpdated = Timestamp,
            NewsRating = (NewsRating)NewsRating,
            NewsHeadline = NewsHeadline,
            NewsTimestamp = NewsTimestamp
        };
    }

    // Create from domain model
    public static StockQuoteEntity FromDomainModel(StockQuote quote)
    {
        return new StockQuoteEntity
        {
            Symbol = quote.Symbol,
            Timestamp = quote.LastUpdated,
            LastPrice = quote.LastPrice,
            Change = quote.Change,
            ChangePercent = quote.ChangePercent,
            Volume = quote.Volume,
            Ask = quote.Ask,
            Bid = quote.Bid,
            High = quote.High,
            Low = quote.Low,
            Open = quote.Open,
            PreviousClose = quote.PreviousClose,
            MarketCap = quote.MarketCap,
            SharesFloat = quote.SharesFloat,
            IsPreMarket = quote.IsPreMarket,
            PreMarketChangePercent = quote.PreMarketChangePercent,
            NewsRating = (int)quote.NewsRating,
            NewsHeadline = quote.NewsHeadline,
            NewsTimestamp = quote.NewsTimestamp
        };
    }
}