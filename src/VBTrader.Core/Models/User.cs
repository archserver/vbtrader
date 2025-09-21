using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VBTrader.Core.Models;

public class User
{
    [Key]
    public int UserId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Salt { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public UserCredentials? SchwabCredentials { get; set; }
    public ICollection<TradeRecord> TradeRecords { get; set; } = new List<TradeRecord>();
    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
    public ICollection<CredentialResetRequest> CredentialResetRequests { get; set; } = new List<CredentialResetRequest>();
}

public class UserCredentials
{
    [Key]
    public int CredentialsId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    [MaxLength(500)]
    public string EncryptedSchwabAppKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string EncryptedSchwabAppSecret { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string CallbackUrl { get; set; } = "https://127.0.0.1:8080";

    [Required]
    [MaxLength(100)]
    public string EncryptionSalt { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public User User { get; set; } = null!;
}

public class UserSession
{
    [Key]
    public int SessionId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    [MaxLength(100)]
    public string SessionToken { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;

    public TradingMode TradingMode { get; set; } = TradingMode.Sandbox;

    // Navigation property
    public User User { get; set; } = null!;
}

public class TradeRecord
{
    [Key]
    public int TradeId { get; set; }

    [Required]
    public int UserId { get; set; }

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

    [Required]
    public TradingMode TradingMode { get; set; }

    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? OrderId { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    // For sandbox trades - reference to historical data used
    public DateTime? HistoricalDataTimestamp { get; set; }

    // Navigation property
    public User User { get; set; } = null!;
}

public enum TradingMode
{
    Sandbox = 0,
    Live = 1
}

public enum TradeAction
{
    Buy = 0,
    Sell = 1
}

public class PasswordResetToken
{
    [Key]
    public int TokenId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Token { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    public bool IsUsed { get; set; } = false;
    public DateTime? UsedAt { get; set; }

    [MaxLength(45)]
    public string? RequestIpAddress { get; set; }

    // Navigation property
    public User User { get; set; } = null!;
}

public class CredentialResetRequest
{
    [Key]
    public int RequestId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public ResetType ResetType { get; set; }

    [Required]
    [MaxLength(100)]
    public string ResetToken { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);

    public bool IsCompleted { get; set; } = false;
    public DateTime? CompletedAt { get; set; }

    [MaxLength(500)]
    public string? ResetReason { get; set; }

    [MaxLength(45)]
    public string? RequestIpAddress { get; set; }

    // Flags for what to preserve during reset
    public bool PreserveTradingHistory { get; set; } = true;
    public bool PreserveSandboxData { get; set; } = true;
    public bool PreserveLiveData { get; set; } = true;

    // Navigation property
    public User User { get; set; } = null!;
}

public class UserAccountBackup
{
    [Key]
    public int BackupId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public BackupReason BackupReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Backup data (JSON serialized)
    [Required]
    public string BackupData { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Notes { get; set; }

    // Auto-cleanup after 90 days
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(90);

    // Navigation property
    public User User { get; set; } = null!;
}

public enum ResetType
{
    PasswordOnly = 0,
    SchwabCredentialsOnly = 1,
    FullAccountReset = 2,
    SchwabCredentialsKeepData = 3
}

public enum BackupReason
{
    PasswordReset = 0,
    CredentialReset = 1,
    AccountDeletion = 2,
    UserRequested = 3,
    SystemMaintenance = 4
}