using VBTrader.Core.Models;

namespace VBTrader.Core.Interfaces;

public interface IHistoricalDataService
{
    /// <summary>
    /// Fetch historical data from API and save to database with enhanced logging
    /// </summary>
    Task<HistoricalDataResult> FetchAndSaveHistoricalDataAsync(
        List<string> symbols,
        DateTime startDate,
        DateTime endDate,
        TimeFrame timeFrame = TimeFrame.OneMinute,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk fetch historical data for multiple symbols with enhanced API features
    /// </summary>
    Task<BulkHistoricalDataResult> FetchBulkHistoricalDataAsync(
        BulkPriceHistoryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get historical data statistics from database
    /// </summary>
    Task<DataStatistics> GetDataStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate data integrity and detect gaps
    /// </summary>
    Task<DataValidationResult> ValidateDataIntegrityAsync(
        string symbol,
        TimeFrame timeFrame,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clean up old cached data based on retention policies
    /// </summary>
    Task<DataCleanupResult> CleanupOldDataAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default);
}

public class HistoricalDataResult
{
    public bool Success { get; set; }
    public int RecordsAdded { get; set; }
    public int RecordsUpdated { get; set; }
    public int RecordsSkipped { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public DataStatistics Statistics { get; set; } = new();
}

public class BulkHistoricalDataResult
{
    public bool Success { get; set; }
    public Dictionary<string, HistoricalDataResult> SymbolResults { get; set; } = new();
    public TimeSpan TotalDuration { get; set; }
    public int TotalApiCalls { get; set; }
    public int SuccessfulSymbols { get; set; }
    public int FailedSymbols { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class DataStatistics
{
    public int UniqueSymbols { get; set; }
    public long TotalRecords { get; set; }
    public long CachedRecords { get; set; }
    public DateTime? OldestData { get; set; }
    public DateTime? NewestData { get; set; }
    public Dictionary<string, int> SymbolCounts { get; set; } = new();
    public Dictionary<string, long> TimeFrameCounts { get; set; } = new();
}

public class DataValidationResult
{
    public bool IsValid { get; set; }
    public List<DataGap> Gaps { get; set; } = new();
    public List<DataInconsistency> Inconsistencies { get; set; } = new();
    public int TotalRecords { get; set; }
    public TimeSpan CoveredTimeSpan { get; set; }
}

public class DataGap
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class DataInconsistency
{
    public DateTime Timestamp { get; set; }
    public string Issue { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public class DataCleanupResult
{
    public bool Success { get; set; }
    public int RecordsDeleted { get; set; }
    public long SpaceFreed { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Errors { get; set; } = new();
}