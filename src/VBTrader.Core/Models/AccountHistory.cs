using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VBTrader.Core.Models;

/// <summary>
/// Historical tracking of all account changes
/// </summary>
[Table("account_history")]
public class AccountHistory
{
    [Key]
    public long HistoryId { get; set; }

    [Required]
    [MaxLength(50)]
    public string AccountNumber { get; set; } = string.Empty;

    [Required]
    public int UserId { get; set; }

    [ForeignKey("UserId")]
    public virtual User User { get; set; } = null!;

    // Account Type and Status
    [MaxLength(20)]
    public string AccountType { get; set; } = string.Empty;

    public int RoundTrips { get; set; }
    public bool IsDayTrader { get; set; }
    public bool IsClosingOnlyRestricted { get; set; }
    public bool PfcbFlag { get; set; }

    // Current Balances (snapshot at this point in time)
    [Column(TypeName = "decimal(18,2)")]
    public decimal AccruedInterest { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CashAvailableForTrading { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CashAvailableForWithdrawal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CashBalance { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal BondValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CashReceipts { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal LiquidationValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal LongOptionMarketValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal LongStockValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal LongMarketValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MoneyMarketFund { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MutualFundValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ShortOptionMarketValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ShortStockValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ShortMarketValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Savings { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalCash { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnsettledCash { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CashDebitCallValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PendingDeposits { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CashCall { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal LongNonMarginableMarketValue { get; set; }

    public bool IsInCall { get; set; }

    // Aggregated Balance
    [Column(TypeName = "decimal(18,2)")]
    public decimal CurrentLiquidationValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal AggregatedLiquidationValue { get; set; }

    // Change tracking
    [Column(TypeName = "decimal(18,2)")]
    public decimal? CashChange { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? LiquidationValueChange { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? TotalValueChange { get; set; }

    [MaxLength(100)]
    public string? ChangeReason { get; set; } // Trading, Deposit, Withdrawal, Market Movement, etc.

    // Metadata
    public DateTime SnapshotTime { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [MaxLength(500)]
    public string? ApiResponseHash { get; set; } // To detect if data actually changed
}