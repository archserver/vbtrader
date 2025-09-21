using VBTrader.Core.Models;

namespace VBTrader.Core.Interfaces;

public interface IUserService
{
    // Authentication
    Task<User?> AuthenticateAsync(string username, string password);
    Task<User> CreateUserAsync(string username, string password, string email);
    Task<bool> ValidateUserAsync(string username, string password);
    Task<UserSession> CreateSessionAsync(int userId, TradingMode tradingMode);
    Task<UserSession?> GetActiveSessionAsync(string sessionToken);
    Task LogoutAsync(string sessionToken);

    // Credential Management
    Task<bool> HasSchwabCredentialsAsync(int userId);
    Task StoreSchwabCredentialsAsync(int userId, string appKey, string appSecret, string callbackUrl);
    Task<(string appKey, string appSecret, string callbackUrl)?> GetSchwabCredentialsAsync(int userId);
    Task UpdateSchwabCredentialsAsync(int userId, string appKey, string appSecret, string callbackUrl);

    // Trading Records
    Task<TradeRecord> RecordTradeAsync(int userId, string symbol, TradeAction action, int quantity, decimal price, TradingMode tradingMode, string? orderId = null, DateTime? historicalTimestamp = null);
    Task<IEnumerable<TradeRecord>> GetTradeHistoryAsync(int userId, DateTime? fromDate = null, DateTime? toDate = null, TradingMode? tradingMode = null);
    Task<Dictionary<string, decimal>> GetCurrentPositionsAsync(int userId, TradingMode tradingMode);
    Task<decimal> GetAccountBalanceAsync(int userId, TradingMode tradingMode);

    // User Management
    Task<User?> GetUserByIdAsync(int userId);
    Task<User?> GetUserByUsernameAsync(string username);
    Task UpdateLastLoginAsync(int userId);

    // Password Reset
    Task<PasswordResetToken> CreatePasswordResetTokenAsync(string email, string ipAddress);
    Task<bool> ValidatePasswordResetTokenAsync(string token);
    Task<bool> ResetPasswordAsync(string token, string newPassword);
    Task<User?> GetUserByEmailAsync(string email);

    // Credential Reset
    Task<CredentialResetRequest> CreateCredentialResetRequestAsync(int userId, ResetType resetType, string email, string reason, string ipAddress, bool preserveTradingHistory = true, bool preserveSandboxData = true, bool preserveLiveData = true);
    Task<bool> ValidateCredentialResetTokenAsync(string token);
    Task<bool> ExecuteCredentialResetAsync(string token, SchwabCredentialsRequest? newCredentials = null);
    Task<IEnumerable<CredentialResetRequest>> GetPendingResetRequestsAsync(int userId);

    // Account Backup & Recovery
    Task<UserAccountBackup> CreateAccountBackupAsync(int userId, BackupReason reason, string? notes = null);
    Task<bool> RestoreAccountFromBackupAsync(int userId, int backupId, bool restoreTradingData = true);
    Task<IEnumerable<UserAccountBackup>> GetAccountBackupsAsync(int userId);
    Task CleanupExpiredBackupsAsync();

    // Account Management
    Task<bool> DeactivateUserAsync(int userId, string reason);
    Task<bool> ReactivateUserAsync(int userId);
    Task<AccountStatistics> GetAccountStatisticsAsync(int userId);
}

public class LoginResult
{
    public bool Success { get; set; }
    public User? User { get; set; }
    public UserSession? Session { get; set; }
    public string? ErrorMessage { get; set; }
    public bool RequiresSchwabSetup { get; set; }
}

public class SchwabCredentialsRequest
{
    public string AppKey { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = "https://127.0.0.1:8080";
}

public class AccountStatistics
{
    public int UserId { get; set; }
    public DateTime AccountCreated { get; set; }
    public DateTime LastLogin { get; set; }
    public int TotalTrades { get; set; }
    public int SandboxTrades { get; set; }
    public int LiveTrades { get; set; }
    public decimal TotalSandboxVolume { get; set; }
    public decimal TotalLiveVolume { get; set; }
    public decimal CurrentSandboxBalance { get; set; }
    public decimal CurrentLiveBalance { get; set; }
    public int ActivePositions { get; set; }
    public Dictionary<string, decimal> TopTradedSymbols { get; set; } = new();
    public bool HasSchwabCredentials { get; set; }
    public DateTime? LastCredentialUpdate { get; set; }
    public int PasswordResetCount { get; set; }
    public int CredentialResetCount { get; set; }
}

public class ResetRequestResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResetToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Instructions { get; set; }
}