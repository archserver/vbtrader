using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using VBTrader.Core.Models;
using VBTrader.Core.Interfaces;

namespace VBTrader.Infrastructure.Database.Entities;

public class SandboxSessionEntity
{
    [Key]
    public int SandboxSessionId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    [MaxLength(200)]
    public string SessionName { get; set; } = string.Empty;

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime CurrentTime { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal InitialBalance { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CurrentBalance { get; set; }

    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    [MaxLength(1000)]
    public string? WatchedSymbols { get; set; }

    // Navigation properties
    public SandboxSettingsEntity Settings { get; set; } = null!;
    public ICollection<SandboxTradeEntity> Trades { get; set; } = new List<SandboxTradeEntity>();
}

public class SandboxSettingsEntity
{
    [Key]
    public int SettingsId { get; set; }

    [Required]
    public int SandboxSessionId { get; set; }

    public bool AutoAdvanceTime { get; set; } = true;
    public int TimeAdvanceIntervalMinutes { get; set; } = 1;
    public bool SkipWeekends { get; set; } = true;
    public bool SkipHolidays { get; set; } = true;
    public bool EnableSlippage { get; set; } = true;

    [Column(TypeName = "decimal(5,2)")]
    public decimal SlippagePercentage { get; set; } = 0.1m;

    public bool EnableCommissions { get; set; } = false;

    [Column(TypeName = "decimal(10,2)")]
    public decimal CommissionPerTrade { get; set; } = 0m;

    public int MaxPositionsPerSymbol { get; set; } = 10000;

    // Navigation property
    public SandboxSessionEntity SandboxSession { get; set; } = null!;
}

public class SandboxTradeEntity
{
    [Key]
    public int TradeId { get; set; }

    [Required]
    public int SandboxSessionId { get; set; }

    [Required]
    [MaxLength(10)]
    public string Symbol { get; set; } = string.Empty;

    [Required]
    public TradeAction Action { get; set; }

    [Required]
    public int Quantity { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,4)")]
    public decimal Price { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalValue { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal Commission { get; set; } = 0;

    public DateTime ExecutedAt { get; set; }

    public OrderType OrderType { get; set; } = OrderType.Market;

    [Column(TypeName = "decimal(18,4)")]
    public decimal? LimitPrice { get; set; }

    // For tracking which historical data point was used
    public DateTime? HistoricalDataTimestamp { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    // Navigation property
    public SandboxSessionEntity SandboxSession { get; set; } = null!;
}

public class HistoricalDataCacheEntity
{
    [Key]
    public long Id { get; set; }

    [Required]
    [MaxLength(10)]
    public string Symbol { get; set; } = string.Empty;

    [Required]
    public DateTime Timestamp { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,4)")]
    public decimal Open { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,4)")]
    public decimal High { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,4)")]
    public decimal Low { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,4)")]
    public decimal Close { get; set; }

    [Required]
    public long Volume { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? AdjustedClose { get; set; }

    public DateTime CachedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(30);

    // Data source information
    [MaxLength(50)]
    public string? DataSource { get; set; }

    [MaxLength(20)]
    public string? TimeFrame { get; set; } = "1min";

    public bool IsPreMarket { get; set; } = false;
    public bool IsAfterHours { get; set; } = false;
}

public class SandboxPerformanceSnapshotEntity
{
    [Key]
    public int SnapshotId { get; set; }

    [Required]
    public int SandboxSessionId { get; set; }

    [Required]
    public DateTime SnapshotTime { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal AccountBalance { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PortfolioValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DayChange { get; set; }

    [Column(TypeName = "decimal(10,4)")]
    public decimal DayChangePercent { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnrealizedPnL { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal RealizedPnL { get; set; }

    public int ActivePositions { get; set; }

    [MaxLength(2000)]
    public string? PositionsSnapshot { get; set; } // JSON serialized positions

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public SandboxSessionEntity SandboxSession { get; set; } = null!;
}