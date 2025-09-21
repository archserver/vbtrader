using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VBTrader.Core.Models;

/// <summary>
/// Current account information - latest snapshot
/// </summary>
[Table("account_information")]
public class AccountInformation
{
    [Key]
    public int AccountInfoId { get; set; }

    [Required]
    [MaxLength(50)]
    public string AccountNumber { get; set; } = string.Empty;

    [Required]
    public int UserId { get; set; }

    [ForeignKey("UserId")]
    public virtual User User { get; set; } = null!;

    // Account Type and Status
    [MaxLength(20)]
    public string AccountType { get; set; } = string.Empty; // CASH, MARGIN, etc.

    public int RoundTrips { get; set; }
    public bool IsDayTrader { get; set; }
    public bool IsClosingOnlyRestricted { get; set; }
    public bool PfcbFlag { get; set; }

    // Current Balances
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

    // Metadata
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Notes { get; set; }
}