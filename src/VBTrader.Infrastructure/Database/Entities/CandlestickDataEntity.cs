using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Extensions.Logging;
using VBTrader.Core.Models;

namespace VBTrader.Infrastructure.Database.Entities;

public class CandlestickDataEntity
{
    public long Id { get; set; }

    [Required]
    [MaxLength(10)]
    public string Symbol { get; set; } = string.Empty;

    // Enhanced timestamp precision
    public DateTime Timestamp { get; set; } // Legacy field - keep for backward compatibility
    [Required]
    public DateTime MarketTimestamp { get; set; } // Market data timestamp with full precision
    [Required]
    public long MarketTimestampMs { get; set; } // EPOCH milliseconds from API
    [Required]
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow; // When retrieved from API
    public DateTime? UpdatedAt { get; set; } // When last updated

    // OHLCV Data
    [Column(TypeName = "decimal(18,6)")]
    public decimal Open { get; set; }
    [Column(TypeName = "decimal(18,6)")]
    public decimal High { get; set; }
    [Column(TypeName = "decimal(18,6)")]
    public decimal Low { get; set; }
    [Column(TypeName = "decimal(18,6)")]
    public decimal Close { get; set; }
    public long Volume { get; set; }

    // Flexible timeframe system (replaces TimeFrameMinutes)
    [Required]
    [MaxLength(20)]
    public string TimeFrameType { get; set; } = "minute"; // "second", "minute", "daily", "weekly", "monthly"
    [Required]
    public int TimeFrameValue { get; set; } = 1; // 1, 5, 15, 30, etc.
    public int? TimeFrameSeconds { get; set; } // Total seconds for any timeframe

    // Legacy field - keep for backward compatibility
    public int TimeFrameMinutes { get; set; } = 1;

    // Data classification
    [Required]
    [MaxLength(20)]
    public string DataType { get; set; } = "ohlc"; // "ohlc", "quote", "tick", "trade"
    public bool IsRealTime { get; set; } = false; // Live vs historical data
    public bool IsExtendedHours { get; set; } = false; // Pre/post market data
    [MaxLength(50)]
    public string? DataSource { get; set; } = "Schwab"; // API source
    [Column(TypeName = "decimal(18,6)")]
    public decimal? PreviousClose { get; set; } // Gap analysis

    // Technical Indicators
    [Column(TypeName = "decimal(18,6)")]
    public decimal? MACD { get; set; }
    [Column(TypeName = "decimal(18,6)")]
    public decimal? MACDSignal { get; set; }
    [Column(TypeName = "decimal(18,6)")]
    public decimal? MACDHistogram { get; set; }
    [Column(TypeName = "decimal(18,6)")]
    public decimal? EMA12 { get; set; }
    [Column(TypeName = "decimal(18,6)")]
    public decimal? EMA26 { get; set; }
    [Column(TypeName = "decimal(18,6)")]
    public decimal? RSI { get; set; }
    [Column(TypeName = "decimal(18,6)")]
    public decimal? BollingerUpper { get; set; }
    [Column(TypeName = "decimal(18,6)")]
    public decimal? BollingerLower { get; set; }
    [Column(TypeName = "decimal(18,6)")]
    public decimal? BollingerMiddle { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Convert to domain model with validation and logging
    public CandlestickData ToDomainModel(ILogger? logger = null)
    {
        try
        {
            logger?.LogDebug("Converting CandlestickDataEntity to domain model for {Symbol} at {MarketTimestamp}",
                Symbol, MarketTimestamp);

            // Validate data integrity
            if (!ValidateOHLCData(logger))
            {
                logger?.LogWarning("OHLC data validation failed for {Symbol} at {MarketTimestamp}",
                    Symbol, MarketTimestamp);
            }

            // Use the enhanced MarketTimestamp for precision
            var timestamp = MarketTimestamp != default ? MarketTimestamp : Timestamp;

            var result = new CandlestickData
            {
                Symbol = Symbol,
                Timestamp = timestamp,
                Open = Open,
                High = High,
                Low = Low,
                Close = Close,
                Volume = Volume,
                MACD = MACD,
                MACDSignal = MACDSignal,
                MACDHistogram = MACDHistogram,
                EMA12 = EMA12,
                EMA26 = EMA26,
                RSI = RSI,
                BollingerUpper = BollingerUpper,
                BollingerLower = BollingerLower,
                BollingerMiddle = BollingerMiddle
            };

            logger?.LogDebug("Successfully converted entity to domain model for {Symbol}", Symbol);
            return result;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error converting CandlestickDataEntity to domain model for {Symbol}", Symbol);
            throw;
        }
    }

    // Create from domain model with enhanced logging and validation
    public static CandlestickDataEntity FromDomainModel(CandlestickData candle, TimeFrame timeFrame = TimeFrame.OneMinute,
        ILogger? logger = null, string? dataSource = "Schwab", bool isRealTime = false, bool isExtendedHours = false)
    {
        try
        {
            if (candle == null)
            {
                logger?.LogError("Cannot create CandlestickDataEntity from null CandlestickData");
                throw new ArgumentNullException(nameof(candle));
            }

            if (string.IsNullOrWhiteSpace(candle.Symbol))
            {
                logger?.LogError("Cannot create CandlestickDataEntity with null or empty Symbol");
                throw new ArgumentException("Symbol cannot be null or empty", nameof(candle));
            }

            logger?.LogDebug("Creating CandlestickDataEntity from domain model for {Symbol} at {Timestamp}",
                candle.Symbol, candle.Timestamp);

            // Convert timestamp to milliseconds for precise storage
            var marketTimestampMs = ((DateTimeOffset)candle.Timestamp).ToUnixTimeMilliseconds();

            // Determine timeframe details
            var (timeFrameType, timeFrameValue, timeFrameSeconds) = GetTimeFrameDetails(timeFrame, logger);

            var entity = new CandlestickDataEntity
            {
                Symbol = candle.Symbol,
                Timestamp = candle.Timestamp, // Legacy field
                MarketTimestamp = candle.Timestamp, // Enhanced precision field
                MarketTimestampMs = marketTimestampMs,
                FetchedAt = DateTime.UtcNow,
                Open = candle.Open,
                High = candle.High,
                Low = candle.Low,
                Close = candle.Close,
                Volume = candle.Volume,
                TimeFrameType = timeFrameType,
                TimeFrameValue = timeFrameValue,
                TimeFrameSeconds = timeFrameSeconds,
                TimeFrameMinutes = (int)timeFrame, // Legacy field
                DataType = "ohlc",
                IsRealTime = isRealTime,
                IsExtendedHours = isExtendedHours,
                DataSource = dataSource,
                MACD = candle.MACD,
                MACDSignal = candle.MACDSignal,
                MACDHistogram = candle.MACDHistogram,
                EMA12 = candle.EMA12,
                EMA26 = candle.EMA26,
                RSI = candle.RSI,
                BollingerUpper = candle.BollingerUpper,
                BollingerLower = candle.BollingerLower,
                BollingerMiddle = candle.BollingerMiddle,
                CreatedAt = DateTime.UtcNow
            };

            // Validate the created entity
            if (!entity.ValidateOHLCData(logger))
            {
                logger?.LogWarning("Created entity failed OHLC validation for {Symbol}", candle.Symbol);
            }

            logger?.LogDebug("Successfully created CandlestickDataEntity for {Symbol} with timeframe {TimeFrameType}:{TimeFrameValue}",
                candle.Symbol, timeFrameType, timeFrameValue);

            return entity;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error creating CandlestickDataEntity from domain model for symbol {Symbol}",
                candle?.Symbol ?? "unknown");
            throw;
        }
    }

    // Validation methods with comprehensive logging
    public bool ValidateTimestamps(ILogger? logger = null)
    {
        try
        {
            var isValid = true;

            // Check if MarketTimestamp is set
            if (MarketTimestamp == default)
            {
                logger?.LogWarning("MarketTimestamp is not set for {Symbol}", Symbol);
                isValid = false;
            }

            // Check if MarketTimestampMs matches MarketTimestamp
            if (MarketTimestampMs > 0)
            {
                var expectedTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(MarketTimestampMs).DateTime;
                var timeDifference = Math.Abs((MarketTimestamp - expectedTimestamp).TotalSeconds);

                if (timeDifference > 1) // Allow 1 second tolerance
                {
                    logger?.LogWarning("MarketTimestamp and MarketTimestampMs mismatch for {Symbol}. " +
                        "Difference: {TimeDifference} seconds", Symbol, timeDifference);
                    isValid = false;
                }
            }

            // Check if FetchedAt is reasonable
            if (FetchedAt > DateTime.UtcNow.AddMinutes(1))
            {
                logger?.LogWarning("FetchedAt is in the future for {Symbol}: {FetchedAt}", Symbol, FetchedAt);
                isValid = false;
            }

            logger?.LogDebug("Timestamp validation {Result} for {Symbol}", isValid ? "passed" : "failed", Symbol);
            return isValid;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error validating timestamps for {Symbol}", Symbol);
            return false;
        }
    }

    public bool ValidateTimeFrame(ILogger? logger = null)
    {
        try
        {
            var isValid = true;

            // Validate TimeFrameType
            var validTimeFrameTypes = new[] { "second", "minute", "daily", "weekly", "monthly" };
            if (!validTimeFrameTypes.Contains(TimeFrameType?.ToLower()))
            {
                logger?.LogWarning("Invalid TimeFrameType '{TimeFrameType}' for {Symbol}", TimeFrameType, Symbol);
                isValid = false;
            }

            // Validate TimeFrameValue
            if (TimeFrameValue <= 0)
            {
                logger?.LogWarning("Invalid TimeFrameValue {TimeFrameValue} for {Symbol}", TimeFrameValue, Symbol);
                isValid = false;
            }

            // Validate TimeFrameSeconds consistency
            if (TimeFrameSeconds.HasValue && TimeFrameSeconds <= 0)
            {
                logger?.LogWarning("Invalid TimeFrameSeconds {TimeFrameSeconds} for {Symbol}", TimeFrameSeconds, Symbol);
                isValid = false;
            }

            logger?.LogDebug("TimeFrame validation {Result} for {Symbol}", isValid ? "passed" : "failed", Symbol);
            return isValid;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error validating timeframe for {Symbol}", Symbol);
            return false;
        }
    }

    public bool ValidateOHLCData(ILogger? logger = null)
    {
        try
        {
            var isValid = true;

            // Check for negative prices
            if (Open < 0 || High < 0 || Low < 0 || Close < 0)
            {
                logger?.LogWarning("Negative prices detected for {Symbol}: O={Open}, H={High}, L={Low}, C={Close}",
                    Symbol, Open, High, Low, Close);
                isValid = false;
            }

            // Check High >= Low
            if (High < Low)
            {
                logger?.LogWarning("High < Low for {Symbol}: H={High}, L={Low}", Symbol, High, Low);
                isValid = false;
            }

            // Check Open and Close are within High/Low range
            if (Open < Low || Open > High)
            {
                logger?.LogWarning("Open price outside High/Low range for {Symbol}: O={Open}, H={High}, L={Low}",
                    Symbol, Open, High, Low);
                isValid = false;
            }

            if (Close < Low || Close > High)
            {
                logger?.LogWarning("Close price outside High/Low range for {Symbol}: C={Close}, H={High}, L={Low}",
                    Symbol, Close, High, Low);
                isValid = false;
            }

            // Check Volume is non-negative
            if (Volume < 0)
            {
                logger?.LogWarning("Negative volume for {Symbol}: {Volume}", Symbol, Volume);
                isValid = false;
            }

            logger?.LogDebug("OHLC validation {Result} for {Symbol}", isValid ? "passed" : "failed", Symbol);
            return isValid;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error validating OHLC data for {Symbol}", Symbol);
            return false;
        }
    }

    // Helper method to determine timeframe details
    private static (string timeFrameType, int timeFrameValue, int? timeFrameSeconds) GetTimeFrameDetails(TimeFrame timeFrame, ILogger? logger = null)
    {
        try
        {
            logger?.LogDebug("Converting TimeFrame {TimeFrame} to detailed timeframe information", timeFrame);

            var result = timeFrame switch
            {
                TimeFrame.OneMinute => ("minute", 1, 60),
                TimeFrame.FiveMinutes => ("minute", 5, 300),
                TimeFrame.TenMinutes => ("minute", 10, 600),
                TimeFrame.FifteenMinutes => ("minute", 15, 900),
                TimeFrame.ThirtyMinutes => ("minute", 30, 1800),
                TimeFrame.OneHour => ("minute", 60, 3600),
                TimeFrame.FourHours => ("minute", 240, 14400),
                TimeFrame.OneDay => ("daily", 1, 86400),
                TimeFrame.Weekly => ("weekly", 1, 604800),
                TimeFrame.Monthly => ("monthly", 1, (int?)null), // Variable length
                _ => ("minute", 1, 60)
            };

            logger?.LogDebug("TimeFrame {TimeFrame} converted to {TimeFrameType}:{TimeFrameValue} ({TimeFrameSeconds} seconds)",
                timeFrame, result.Item1, result.Item2, result.Item3);

            return result;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error converting TimeFrame {TimeFrame} to detailed information", timeFrame);
            return ("minute", 1, 60); // Safe default
        }
    }

    // Comprehensive validation method
    public bool ValidateEntity(ILogger? logger = null)
    {
        try
        {
            logger?.LogDebug("Performing comprehensive validation for {Symbol} entity", Symbol);

            var timestampValid = ValidateTimestamps(logger);
            var timeFrameValid = ValidateTimeFrame(logger);
            var ohlcValid = ValidateOHLCData(logger);

            var isValid = timestampValid && timeFrameValid && ohlcValid;

            logger?.LogInformation("Comprehensive validation {Result} for {Symbol}",
                isValid ? "passed" : "failed", Symbol);

            return isValid;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error during comprehensive validation for {Symbol}", Symbol);
            return false;
        }
    }
}