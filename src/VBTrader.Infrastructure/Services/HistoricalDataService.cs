using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VBTrader.Core.Interfaces;
using VBTrader.Core.Models;
using VBTrader.Infrastructure.Database;
using VBTrader.Infrastructure.Database.Entities;

namespace VBTrader.Infrastructure.Services;

public class HistoricalDataService : IHistoricalDataService
{
    private readonly ILogger<HistoricalDataService> _logger;
    private readonly ISchwabApiClient _schwabClient;
    private readonly VBTraderDbContext _dbContext;

    private const int MAX_SYMBOLS = 5;
    private const int MAX_RETRIES = 3;
    private const int BATCH_SIZE = 1000;
    private readonly TimeSpan _defaultRetentionPeriod = TimeSpan.FromDays(30);

    public HistoricalDataService(
        ILogger<HistoricalDataService> logger,
        ISchwabApiClient schwabClient,
        VBTraderDbContext dbContext)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _schwabClient = schwabClient ?? throw new ArgumentNullException(nameof(schwabClient));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

        _logger.LogDebug("HistoricalDataService initialized with max symbols: {MaxSymbols}, batch size: {BatchSize}",
            MAX_SYMBOLS, BATCH_SIZE);
    }

    public async Task<HistoricalDataResult> FetchAndSaveHistoricalDataAsync(
        List<string> symbols,
        DateTime startDate,
        DateTime endDate,
        TimeFrame timeFrame = TimeFrame.OneMinute,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new HistoricalDataResult();

        try
        {
            _logger.LogInformation("Starting historical data fetch for {SymbolCount} symbols from {StartDate} to {EndDate} with timeframe {TimeFrame}",
                symbols.Count, startDate, endDate, timeFrame);

            // Input validation
            if (!ValidateInputParameters(symbols, startDate, endDate, result))
            {
                return result;
            }

            var limitedSymbols = symbols.Take(MAX_SYMBOLS).ToList();
            if (symbols.Count > MAX_SYMBOLS)
            {
                _logger.LogWarning("Symbol count {Count} exceeds maximum {Max}. Processing first {Max} symbols",
                    symbols.Count, MAX_SYMBOLS, MAX_SYMBOLS);
                result.Warnings.Add($"Limited to first {MAX_SYMBOLS} symbols");
            }

            // Process each symbol individually with comprehensive logging
            foreach (var symbol in limitedSymbols)
            {
                try
                {
                    _logger.LogDebug("Processing symbol {Symbol}", symbol);
                    var symbolResult = await ProcessSymbolAsync(symbol, startDate, endDate, timeFrame, cancellationToken);

                    result.RecordsAdded += symbolResult.RecordsAdded;
                    result.RecordsUpdated += symbolResult.RecordsUpdated;
                    result.RecordsSkipped += symbolResult.RecordsSkipped;
                    result.Errors.AddRange(symbolResult.Errors);
                    result.Warnings.AddRange(symbolResult.Warnings);

                    _logger.LogInformation("Completed processing {Symbol}: {Added} added, {Updated} updated, {Skipped} skipped",
                        symbol, symbolResult.RecordsAdded, symbolResult.RecordsUpdated, symbolResult.RecordsSkipped);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing symbol {Symbol}", symbol);
                    result.Errors.Add($"Failed to process {symbol}: {ex.Message}");
                }
            }

            // Get final statistics
            result.Statistics = await GetDataStatisticsAsync(cancellationToken);
            result.Success = result.Errors.Count == 0;
            result.Duration = stopwatch.Elapsed;

            _logger.LogInformation("Historical data fetch completed in {Duration}. Success: {Success}, Total records: {Total}",
                result.Duration, result.Success, result.RecordsAdded + result.RecordsUpdated);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during historical data fetch");
            result.Success = false;
            result.Errors.Add($"Fatal error: {ex.Message}");
            result.Duration = stopwatch.Elapsed;
            return result;
        }
    }

    public async Task<BulkHistoricalDataResult> FetchBulkHistoricalDataAsync(
        BulkPriceHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new BulkHistoricalDataResult();

        try
        {
            _logger.LogInformation("Starting bulk historical data fetch for {SymbolCount} symbols with {PeriodType}:{Period} {FrequencyType}:{Frequency}",
                request.Symbols.Count, request.PeriodType, request.Period, request.FrequencyType, request.Frequency);

            // Use the enhanced bulk API method
            var bulkData = await _schwabClient.GetBulkPriceHistoryAsync(request);
            result.TotalApiCalls = 1; // Bulk API uses single call

            // Process each symbol's data
            foreach (var symbolData in bulkData)
            {
                try
                {
                    _logger.LogDebug("Processing bulk data for symbol {Symbol} with {Count} records",
                        symbolData.Key, symbolData.Value.Count());

                    var symbolResult = await SaveCandlestickDataAsync(
                        symbolData.Key,
                        symbolData.Value,
                        ConvertToTimeFrame(request.FrequencyType, request.Frequency),
                        request.NeedExtendedHoursData,
                        cancellationToken);

                    result.SymbolResults[symbolData.Key] = symbolResult;

                    if (symbolResult.Success)
                    {
                        result.SuccessfulSymbols++;
                    }
                    else
                    {
                        result.FailedSymbols++;
                        result.Errors.AddRange(symbolResult.Errors);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing bulk data for symbol {Symbol}", symbolData.Key);
                    result.FailedSymbols++;
                    result.Errors.Add($"Failed to process {symbolData.Key}: {ex.Message}");
                }
            }

            result.TotalDuration = stopwatch.Elapsed;
            result.Success = result.FailedSymbols == 0;

            _logger.LogInformation("Bulk historical data fetch completed in {Duration}. Successful: {Success}/{Total}",
                result.TotalDuration, result.SuccessfulSymbols, result.SuccessfulSymbols + result.FailedSymbols);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during bulk historical data fetch");
            result.Success = false;
            result.Errors.Add($"Fatal error: {ex.Message}");
            result.TotalDuration = stopwatch.Elapsed;
            return result;
        }
    }

    public async Task<DataStatistics> GetDataStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving data statistics from database");

            var statistics = new DataStatistics();

            // Get basic counts
            statistics.TotalRecords = await _dbContext.CandlestickData.CountAsync(cancellationToken);
            statistics.UniqueSymbols = await _dbContext.CandlestickData
                .Select(c => c.Symbol)
                .Distinct()
                .CountAsync(cancellationToken);

            // Get date range
            if (statistics.TotalRecords > 0)
            {
                statistics.OldestData = await _dbContext.CandlestickData
                    .MinAsync(c => c.MarketTimestamp, cancellationToken);
                statistics.NewestData = await _dbContext.CandlestickData
                    .MaxAsync(c => c.MarketTimestamp, cancellationToken);
            }

            // Get symbol counts
            statistics.SymbolCounts = await _dbContext.CandlestickData
                .GroupBy(c => c.Symbol)
                .ToDictionaryAsync(g => g.Key, g => g.Count(), cancellationToken);

            // Get timeframe counts
            statistics.TimeFrameCounts = await _dbContext.CandlestickData
                .GroupBy(c => c.TimeFrameType + ":" + c.TimeFrameValue.ToString())
                .ToDictionaryAsync(g => g.Key, g => (long)g.Count(), cancellationToken);

            _logger.LogDebug("Retrieved data statistics: {TotalRecords} records, {UniqueSymbols} symbols",
                statistics.TotalRecords, statistics.UniqueSymbols);

            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving data statistics");
            throw;
        }
    }

    public async Task<DataValidationResult> ValidateDataIntegrityAsync(
        string symbol,
        TimeFrame timeFrame,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Validating data integrity for {Symbol} from {StartDate} to {EndDate}",
                symbol, startDate, endDate);

            var result = new DataValidationResult();
            var (timeFrameType, timeFrameValue, _) = GetTimeFrameDetails(timeFrame);

            // Get all records for the symbol and timeframe
            var records = await _dbContext.CandlestickData
                .Where(c => c.Symbol == symbol &&
                           c.TimeFrameType == timeFrameType &&
                           c.TimeFrameValue == timeFrameValue &&
                           c.MarketTimestamp >= startDate &&
                           c.MarketTimestamp <= endDate)
                .OrderBy(c => c.MarketTimestamp)
                .ToListAsync(cancellationToken);

            result.TotalRecords = records.Count;

            if (records.Count == 0)
            {
                result.IsValid = false;
                result.Gaps.Add(new DataGap
                {
                    StartTime = startDate,
                    EndTime = endDate,
                    Duration = endDate - startDate,
                    Reason = "No data found"
                });
                return result;
            }

            // Detect gaps and validate data integrity
            await DetectDataGaps(records, timeFrame, result, cancellationToken);
            await ValidateOHLCConsistency(records, result, cancellationToken);

            result.CoveredTimeSpan = records.Last().MarketTimestamp - records.First().MarketTimestamp;
            result.IsValid = result.Gaps.Count == 0 && result.Inconsistencies.Count == 0;

            _logger.LogInformation("Data validation completed for {Symbol}. Valid: {IsValid}, Gaps: {Gaps}, Issues: {Issues}",
                symbol, result.IsValid, result.Gaps.Count, result.Inconsistencies.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating data integrity for {Symbol}", symbol);
            throw;
        }
    }

    public async Task<DataCleanupResult> CleanupOldDataAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new DataCleanupResult();

        try
        {
            var cutoffDate = DateTime.UtcNow - retentionPeriod;
            _logger.LogInformation("Starting data cleanup for records older than {CutoffDate} (retention: {RetentionPeriod})",
                cutoffDate, retentionPeriod);

            // Find old records
            var oldRecords = await _dbContext.CandlestickData
                .Where(c => c.CreatedAt < cutoffDate)
                .ToListAsync(cancellationToken);

            if (oldRecords.Count == 0)
            {
                _logger.LogInformation("No old records found for cleanup");
                result.Success = true;
                return result;
            }

            // Calculate space to be freed (approximate)
            result.SpaceFreed = oldRecords.Count * 200; // Rough estimate per record

            // Delete in batches
            for (int i = 0; i < oldRecords.Count; i += BATCH_SIZE)
            {
                var batch = oldRecords.Skip(i).Take(BATCH_SIZE).ToList();
                _dbContext.CandlestickData.RemoveRange(batch);

                await _dbContext.SaveChangesAsync(cancellationToken);
                result.RecordsDeleted += batch.Count;

                _logger.LogDebug("Deleted batch of {BatchSize} records ({Total}/{TotalRecords})",
                    batch.Count, result.RecordsDeleted, oldRecords.Count);
            }

            result.Duration = stopwatch.Elapsed;
            result.Success = true;

            _logger.LogInformation("Data cleanup completed in {Duration}. Deleted {RecordsDeleted} records, freed ~{SpaceFreed} bytes",
                result.Duration, result.RecordsDeleted, result.SpaceFreed);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during data cleanup");
            result.Success = false;
            result.Errors.Add($"Cleanup error: {ex.Message}");
            result.Duration = stopwatch.Elapsed;
            return result;
        }
    }

    // Private helper methods with comprehensive logging
    private bool ValidateInputParameters(List<string> symbols, DateTime startDate, DateTime endDate, HistoricalDataResult result)
    {
        if (symbols == null || symbols.Count == 0)
        {
            _logger.LogError("No symbols provided for historical data fetch");
            result.Errors.Add("No symbols provided");
            return false;
        }

        if (startDate >= endDate)
        {
            _logger.LogError("Invalid date range: start date {StartDate} is not before end date {EndDate}", startDate, endDate);
            result.Errors.Add("Start date must be before end date");
            return false;
        }

        if (endDate > DateTime.UtcNow)
        {
            _logger.LogWarning("End date {EndDate} is in the future, adjusting to current time", endDate);
            result.Warnings.Add("End date adjusted to current time");
        }

        return true;
    }

    private async Task<HistoricalDataResult> ProcessSymbolAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        TimeFrame timeFrame,
        CancellationToken cancellationToken)
    {
        var symbolResult = new HistoricalDataResult();

        try
        {
            _logger.LogDebug("Fetching historical data for {Symbol} from API", symbol);

            // Use enhanced API method
            var priceHistory = await _schwabClient.GetPriceHistoryAsync(symbol, timeFrame, startDate, endDate);

            if (!priceHistory.Any())
            {
                _logger.LogWarning("No historical data returned for {Symbol}", symbol);
                symbolResult.Warnings.Add($"No data available for {symbol}");
                return symbolResult;
            }

            _logger.LogDebug("Retrieved {Count} records for {Symbol}, saving to database", priceHistory.Count(), symbol);

            // Save to database using enhanced entity
            symbolResult = await SaveCandlestickDataAsync(symbol, priceHistory, timeFrame, false, cancellationToken);
            symbolResult.Success = true;

            return symbolResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing symbol {Symbol}", symbol);
            symbolResult.Errors.Add($"API error for {symbol}: {ex.Message}");
            return symbolResult;
        }
    }

    private async Task<HistoricalDataResult> SaveCandlestickDataAsync(
        string symbol,
        IEnumerable<CandlestickData> candlestickData,
        TimeFrame timeFrame,
        bool isExtendedHours,
        CancellationToken cancellationToken)
    {
        var result = new HistoricalDataResult();

        try
        {
            var dataList = candlestickData.ToList();
            _logger.LogDebug("Saving {Count} candlestick records for {Symbol}", dataList.Count, symbol);

            foreach (var candle in dataList)
            {
                try
                {
                    // Check if record already exists
                    var existing = await _dbContext.CandlestickData
                        .FirstOrDefaultAsync(c => c.Symbol == symbol &&
                                                c.MarketTimestamp == candle.Timestamp &&
                                                c.TimeFrameType == GetTimeFrameDetails(timeFrame).timeFrameType &&
                                                c.TimeFrameValue == GetTimeFrameDetails(timeFrame).timeFrameValue,
                                            cancellationToken);

                    if (existing != null)
                    {
                        // Update existing record
                        UpdateExistingRecord(existing, candle, isExtendedHours);
                        result.RecordsUpdated++;
                        _logger.LogDebug("Updated existing record for {Symbol} at {Timestamp}", symbol, candle.Timestamp);
                    }
                    else
                    {
                        // Create new record with enhanced entity
                        var entity = CandlestickDataEntity.FromDomainModel(
                            candle, timeFrame, _logger, "Schwab", false, isExtendedHours);

                        // Validate entity before saving
                        if (!entity.ValidateEntity(_logger))
                        {
                            _logger.LogWarning("Entity validation failed for {Symbol} at {Timestamp}", symbol, candle.Timestamp);
                            result.Warnings.Add($"Validation warning for {symbol} at {candle.Timestamp}");
                        }

                        _dbContext.CandlestickData.Add(entity);
                        result.RecordsAdded++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving individual record for {Symbol} at {Timestamp}", symbol, candle.Timestamp);
                    result.Errors.Add($"Save error for {symbol} at {candle.Timestamp}: {ex.Message}");
                }
            }

            // Save all changes in a single transaction
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Successfully saved candlestick data for {Symbol}: {Added} added, {Updated} updated",
                symbol, result.RecordsAdded, result.RecordsUpdated);

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving candlestick data for {Symbol}", symbol);
            result.Errors.Add($"Database error for {symbol}: {ex.Message}");
            return result;
        }
    }

    private void UpdateExistingRecord(CandlestickDataEntity existing, CandlestickData newData, bool isExtendedHours)
    {
        existing.Open = newData.Open;
        existing.High = newData.High;
        existing.Low = newData.Low;
        existing.Close = newData.Close;
        existing.Volume = newData.Volume;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.IsExtendedHours = isExtendedHours;

        // Update technical indicators if provided
        if (newData.MACD.HasValue) existing.MACD = newData.MACD;
        if (newData.MACDSignal.HasValue) existing.MACDSignal = newData.MACDSignal;
        if (newData.MACDHistogram.HasValue) existing.MACDHistogram = newData.MACDHistogram;
        if (newData.EMA12.HasValue) existing.EMA12 = newData.EMA12;
        if (newData.EMA26.HasValue) existing.EMA26 = newData.EMA26;
        if (newData.RSI.HasValue) existing.RSI = newData.RSI;
        if (newData.BollingerUpper.HasValue) existing.BollingerUpper = newData.BollingerUpper;
        if (newData.BollingerLower.HasValue) existing.BollingerLower = newData.BollingerLower;
        if (newData.BollingerMiddle.HasValue) existing.BollingerMiddle = newData.BollingerMiddle;
    }

    private async Task DetectDataGaps(List<CandlestickDataEntity> records, TimeFrame timeFrame, DataValidationResult result, CancellationToken cancellationToken)
    {
        if (records.Count < 2) return;

        var expectedInterval = GetExpectedInterval(timeFrame);

        for (int i = 1; i < records.Count; i++)
        {
            var gap = records[i].MarketTimestamp - records[i - 1].MarketTimestamp;
            if (gap > expectedInterval * 1.5) // Allow 50% tolerance
            {
                result.Gaps.Add(new DataGap
                {
                    StartTime = records[i - 1].MarketTimestamp,
                    EndTime = records[i].MarketTimestamp,
                    Duration = gap,
                    Reason = "Missing data interval"
                });

                _logger.LogDebug("Data gap detected from {StartTime} to {EndTime} (duration: {Duration})",
                    records[i - 1].MarketTimestamp, records[i].MarketTimestamp, gap);
            }
        }
    }

    private async Task ValidateOHLCConsistency(List<CandlestickDataEntity> records, DataValidationResult result, CancellationToken cancellationToken)
    {
        foreach (var record in records)
        {
            if (!record.ValidateOHLCData(_logger))
            {
                result.Inconsistencies.Add(new DataInconsistency
                {
                    Timestamp = record.MarketTimestamp,
                    Issue = "OHLC validation failed",
                    Details = $"O:{record.Open}, H:{record.High}, L:{record.Low}, C:{record.Close}"
                });
            }
        }
    }

    private TimeSpan GetExpectedInterval(TimeFrame timeFrame)
    {
        return timeFrame switch
        {
            TimeFrame.OneMinute => TimeSpan.FromMinutes(1),
            TimeFrame.FiveMinutes => TimeSpan.FromMinutes(5),
            TimeFrame.TenMinutes => TimeSpan.FromMinutes(10),
            TimeFrame.FifteenMinutes => TimeSpan.FromMinutes(15),
            TimeFrame.ThirtyMinutes => TimeSpan.FromMinutes(30),
            TimeFrame.OneHour => TimeSpan.FromHours(1),
            TimeFrame.OneDay => TimeSpan.FromDays(1),
            _ => TimeSpan.FromMinutes(1)
        };
    }

    private TimeFrame ConvertToTimeFrame(FrequencyType frequencyType, int frequency)
    {
        return frequencyType switch
        {
            FrequencyType.Minute when frequency == 1 => TimeFrame.OneMinute,
            FrequencyType.Minute when frequency == 5 => TimeFrame.FiveMinutes,
            FrequencyType.Minute when frequency == 10 => TimeFrame.TenMinutes,
            FrequencyType.Minute when frequency == 15 => TimeFrame.FifteenMinutes,
            FrequencyType.Minute when frequency == 30 => TimeFrame.ThirtyMinutes,
            FrequencyType.Minute when frequency == 60 => TimeFrame.OneHour,
            FrequencyType.Daily => TimeFrame.OneDay,
            FrequencyType.Weekly => TimeFrame.Weekly,
            FrequencyType.Monthly => TimeFrame.Monthly,
            _ => TimeFrame.OneMinute
        };
    }

    private static (string timeFrameType, int timeFrameValue, int? timeFrameSeconds) GetTimeFrameDetails(TimeFrame timeFrame)
    {
        return timeFrame switch
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
            TimeFrame.Monthly => ("monthly", 1, null),
            _ => ("minute", 1, 60)
        };
    }
}