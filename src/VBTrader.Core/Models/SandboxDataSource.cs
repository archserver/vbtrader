using System;
using System.Collections.Generic;

namespace VBTrader.Core.Models;

public enum SandboxDataSource
{
    LiveMarket,        // Use current day's real market data from Schwab
    Database,          // Use historical data from PostgreSQL database
    HistoricalMinute,  // Use minute-by-minute historical data from Schwab API
    SimulatedRandom    // Use simulated random data for testing
}

public class SandboxConfiguration
{
    public SandboxDataSource DataSource { get; set; } = SandboxDataSource.LiveMarket;
    public DateTime? HistoricalDate { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public List<string> Symbols { get; set; } = new();
    public int PlaybackSpeed { get; set; } = 1; // 1x, 2x, 5x, 10x speed
    public bool PauseOnStart { get; set; } = true;

    // For database replay
    public string? DatabaseQueryFilter { get; set; }

    // For historical minute data
    public int MinutesInterval { get; set; } = 1; // 1, 5, 15, 30, 60 minutes
}