using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VBTrader.Core.Interfaces;
using VBTrader.Core.Models;
using VBTrader.Infrastructure.Database;
using VBTrader.Security.Cryptography;
using System.Security.Cryptography;
using System.Text.Json;

namespace VBTrader.Infrastructure.Services;

public class UserService : IUserService
{
    private readonly VBTraderDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<UserService> _logger;

    public UserService(VBTraderDbContext context, IPasswordHasher passwordHasher, ILogger<UserService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<User?> AuthenticateAsync(string username, string password)
    {
        try
        {
            var user = await _context.Users
                .Include(u => u.SchwabCredentials)
                .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

            if (user == null)
                return null;

            var isValid = _passwordHasher.VerifyPassword(password, user.PasswordHash);

            if (!isValid)
                return null;

            await UpdateLastLoginAsync(user.UserId);
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error authenticating user: {Username}", username);
            return null;
        }
    }

    public async Task<User> CreateUserAsync(string username, string password, string email)
    {
        var existingUser = await GetUserByUsernameAsync(username);
        if (existingUser != null)
            throw new InvalidOperationException("Username already exists");

        var passwordHash = _passwordHasher.HashPassword(password);

        var user = new User
        {
            Username = username,
            PasswordHash = passwordHash,
            Salt = "", // Salt is embedded in the passwordHash, not stored separately
            Email = email,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created new user: {Username}", username);

        return user;
    }

    public async Task<bool> ValidateUserAsync(string username, string password)
    {
        var user = await AuthenticateAsync(username, password);
        return user != null;
    }

    public async Task<UserSession> CreateSessionAsync(int userId, TradingMode tradingMode)
    {
        // Deactivate existing sessions
        var existingSessions = await _context.UserSessions
            .Where(s => s.UserId == userId && s.IsActive)
            .ToListAsync();

        foreach (var session in existingSessions)
        {
            session.IsActive = false;
        }

        var sessionToken = GenerateSessionToken();
        var newSession = new UserSession
        {
            UserId = userId,
            SessionToken = sessionToken,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            IsActive = true,
            TradingMode = tradingMode
        };

        _context.UserSessions.Add(newSession);
        await _context.SaveChangesAsync();

        return newSession;
    }

    public async Task<UserSession?> GetActiveSessionAsync(string sessionToken)
    {
        return await _context.UserSessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.SessionToken == sessionToken &&
                                     s.IsActive &&
                                     s.ExpiresAt > DateTime.UtcNow);
    }

    public async Task LogoutAsync(string sessionToken)
    {
        var session = await _context.UserSessions
            .FirstOrDefaultAsync(s => s.SessionToken == sessionToken);

        if (session != null)
        {
            session.IsActive = false;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> HasSchwabCredentialsAsync(int userId)
    {
        return await _context.UserCredentials
            .AnyAsync(c => c.UserId == userId);
    }

    public async Task StoreSchwabCredentialsAsync(int userId, string appKey, string appSecret, string callbackUrl)
    {
        var encryptionSalt = GenerateSalt();
        var encryptedAppKey = EncryptData(appKey, encryptionSalt);
        var encryptedAppSecret = EncryptData(appSecret, encryptionSalt);

        var existing = await _context.UserCredentials
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (existing != null)
        {
            existing.EncryptedSchwabAppKey = encryptedAppKey;
            existing.EncryptedSchwabAppSecret = encryptedAppSecret;
            existing.CallbackUrl = callbackUrl;
            existing.EncryptionSalt = encryptionSalt;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var credentials = new UserCredentials
            {
                UserId = userId,
                EncryptedSchwabAppKey = encryptedAppKey,
                EncryptedSchwabAppSecret = encryptedAppSecret,
                CallbackUrl = callbackUrl,
                EncryptionSalt = encryptionSalt,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.UserCredentials.Add(credentials);
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Stored Schwab credentials for user: {UserId}", userId);
    }

    public async Task<(string appKey, string appSecret, string callbackUrl)?> GetSchwabCredentialsAsync(int userId)
    {
        try
        {
            var credentials = await _context.UserCredentials
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (credentials == null)
                return null;

            var appKey = DecryptData(credentials.EncryptedSchwabAppKey, credentials.EncryptionSalt);
            var appSecret = DecryptData(credentials.EncryptedSchwabAppSecret, credentials.EncryptionSalt);

            return (appKey, appSecret, credentials.CallbackUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Schwab credentials for user: {UserId}", userId);
            return null;
        }
    }

    public async Task UpdateSchwabCredentialsAsync(int userId, string appKey, string appSecret, string callbackUrl)
    {
        await StoreSchwabCredentialsAsync(userId, appKey, appSecret, callbackUrl);
    }

    public async Task<TradeRecord> RecordTradeAsync(int userId, string symbol, TradeAction action, int quantity, decimal price, TradingMode tradingMode, string? orderId = null, DateTime? historicalTimestamp = null)
    {
        var trade = new TradeRecord
        {
            UserId = userId,
            Symbol = symbol,
            Action = action,
            Quantity = quantity,
            Price = price,
            TotalValue = price * quantity,
            TradingMode = tradingMode,
            ExecutedAt = DateTime.UtcNow,
            OrderId = orderId,
            HistoricalDataTimestamp = historicalTimestamp
        };

        _context.TradeRecords.Add(trade);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Recorded {Action} trade: {Quantity} {Symbol} @ ${Price} for user {UserId} in {Mode} mode",
            action, quantity, symbol, price, userId, tradingMode);

        return trade;
    }

    public async Task<IEnumerable<TradeRecord>> GetTradeHistoryAsync(int userId, DateTime? fromDate = null, DateTime? toDate = null, TradingMode? tradingMode = null)
    {
        var query = _context.TradeRecords.Where(t => t.UserId == userId);

        if (fromDate.HasValue)
            query = query.Where(t => t.ExecutedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(t => t.ExecutedAt <= toDate.Value);

        if (tradingMode.HasValue)
            query = query.Where(t => t.TradingMode == tradingMode.Value);

        return await query.OrderByDescending(t => t.ExecutedAt).ToListAsync();
    }

    public async Task<Dictionary<string, decimal>> GetCurrentPositionsAsync(int userId, TradingMode tradingMode)
    {
        var trades = await _context.TradeRecords
            .Where(t => t.UserId == userId && t.TradingMode == tradingMode)
            .GroupBy(t => t.Symbol)
            .Select(g => new
            {
                Symbol = g.Key,
                NetQuantity = g.Sum(t => t.Action == TradeAction.Buy ? t.Quantity : -t.Quantity)
            })
            .Where(p => p.NetQuantity != 0)
            .ToDictionaryAsync(p => p.Symbol, p => (decimal)p.NetQuantity);

        return trades;
    }

    public async Task<decimal> GetAccountBalanceAsync(int userId, TradingMode tradingMode)
    {
        // For sandbox mode, start with a virtual balance
        const decimal initialSandboxBalance = 100000m;

        var totalSpent = await _context.TradeRecords
            .Where(t => t.UserId == userId && t.TradingMode == tradingMode)
            .SumAsync(t => t.Action == TradeAction.Buy ? t.TotalValue : -t.TotalValue);

        return tradingMode == TradingMode.Sandbox
            ? initialSandboxBalance - totalSpent
            : -totalSpent; // For live mode, we'd need to integrate with actual account balance
    }

    public async Task<User?> GetUserByIdAsync(int userId)
    {
        return await _context.Users
            .Include(u => u.SchwabCredentials)
            .FirstOrDefaultAsync(u => u.UserId == userId);
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        return await _context.Users
            .Include(u => u.SchwabCredentials)
            .FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task UpdateLastLoginAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user != null)
        {
            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    // Helper methods
    private static string GenerateSalt()
    {
        const int saltSize = 32;
        using var rng = RandomNumberGenerator.Create();
        var saltBytes = new byte[saltSize];
        rng.GetBytes(saltBytes);
        return Convert.ToBase64String(saltBytes);
    }

    private static string GenerateSessionToken()
    {
        const int tokenSize = 64;
        using var rng = RandomNumberGenerator.Create();
        var tokenBytes = new byte[tokenSize];
        rng.GetBytes(tokenBytes);
        return Convert.ToBase64String(tokenBytes);
    }

    private static string EncryptData(string data, string salt)
    {
        // For now, use a simple encryption with the salt as key
        // In production, consider using more robust encryption like AES
        var dataBytes = System.Text.Encoding.UTF8.GetBytes(data);
        var saltBytes = Convert.FromBase64String(salt);

        var result = new byte[dataBytes.Length];
        for (int i = 0; i < dataBytes.Length; i++)
        {
            result[i] = (byte)(dataBytes[i] ^ saltBytes[i % saltBytes.Length]);
        }

        return Convert.ToBase64String(result);
    }

    private static string DecryptData(string encryptedData, string salt)
    {
        // Reverse of the encryption process
        var encryptedBytes = Convert.FromBase64String(encryptedData);
        var saltBytes = Convert.FromBase64String(salt);

        var result = new byte[encryptedBytes.Length];
        for (int i = 0; i < encryptedBytes.Length; i++)
        {
            result[i] = (byte)(encryptedBytes[i] ^ saltBytes[i % saltBytes.Length]);
        }

        return System.Text.Encoding.UTF8.GetString(result);
    }

    // Password Reset Implementation
    public async Task<PasswordResetToken> CreatePasswordResetTokenAsync(string email, string ipAddress)
    {
        var user = await GetUserByEmailAsync(email);
        if (user == null)
            throw new InvalidOperationException("User not found");

        // Invalidate any existing reset tokens
        var existingTokens = await _context.PasswordResetTokens
            .Where(t => t.UserId == user.UserId && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();

        foreach (var token in existingTokens)
        {
            token.IsUsed = true;
            token.UsedAt = DateTime.UtcNow;
        }

        var resetToken = new PasswordResetToken
        {
            UserId = user.UserId,
            Token = GenerateSecureToken(),
            Email = email,
            RequestIpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        _context.PasswordResetTokens.Add(resetToken);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Password reset token created for user: {UserId}, Email: {Email}", user.UserId, email);
        return resetToken;
    }

    public async Task<bool> ValidatePasswordResetTokenAsync(string token)
    {
        var resetToken = await _context.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.Token == token && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow);

        return resetToken != null;
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword)
    {
        var resetToken = await _context.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow);

        if (resetToken == null)
            return false;

        // Update user password
        var newPasswordHash = _passwordHasher.HashPassword(newPassword);
        var newSalt = Convert.ToBase64String(_passwordHasher.GenerateSalt());

        resetToken.User.PasswordHash = newPasswordHash;
        resetToken.User.Salt = newSalt;

        // Mark token as used
        resetToken.IsUsed = true;
        resetToken.UsedAt = DateTime.UtcNow;

        // Invalidate all user sessions
        var userSessions = await _context.UserSessions
            .Where(s => s.UserId == resetToken.UserId && s.IsActive)
            .ToListAsync();

        foreach (var session in userSessions)
        {
            session.IsActive = false;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Password reset completed for user: {UserId}", resetToken.UserId);
        return true;
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        return await _context.Users
            .Include(u => u.SchwabCredentials)
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
    }

    // Credential Reset Implementation
    public async Task<CredentialResetRequest> CreateCredentialResetRequestAsync(int userId, ResetType resetType, string email, string reason, string ipAddress, bool preserveTradingHistory = true, bool preserveSandboxData = true, bool preserveLiveData = true)
    {
        var user = await GetUserByIdAsync(userId);
        if (user == null)
            throw new InvalidOperationException("User not found");

        // Create backup before reset if preserving data
        if (preserveTradingHistory || preserveSandboxData || preserveLiveData)
        {
            await CreateAccountBackupAsync(userId, BackupReason.CredentialReset, $"Pre-reset backup: {reason}");
        }

        // Invalidate existing credential reset requests
        var existingRequests = await _context.CredentialResetRequests
            .Where(r => r.UserId == userId && !r.IsCompleted && r.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();

        foreach (var request in existingRequests)
        {
            request.IsCompleted = true;
            request.CompletedAt = DateTime.UtcNow;
        }

        var resetRequest = new CredentialResetRequest
        {
            UserId = userId,
            ResetType = resetType,
            ResetToken = GenerateSecureToken(),
            Email = email,
            ResetReason = reason,
            RequestIpAddress = ipAddress,
            PreserveTradingHistory = preserveTradingHistory,
            PreserveSandboxData = preserveSandboxData,
            PreserveLiveData = preserveLiveData,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        _context.CredentialResetRequests.Add(resetRequest);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Credential reset request created for user: {UserId}, Type: {ResetType}", userId, resetType);
        return resetRequest;
    }

    public async Task<bool> ValidateCredentialResetTokenAsync(string token)
    {
        var resetRequest = await _context.CredentialResetRequests
            .FirstOrDefaultAsync(r => r.ResetToken == token && !r.IsCompleted && r.ExpiresAt > DateTime.UtcNow);

        return resetRequest != null;
    }

    public async Task<bool> ExecuteCredentialResetAsync(string token, SchwabCredentialsRequest? newCredentials = null)
    {
        var resetRequest = await _context.CredentialResetRequests
            .Include(r => r.User)
            .ThenInclude(u => u.SchwabCredentials)
            .FirstOrDefaultAsync(r => r.ResetToken == token && !r.IsCompleted && r.ExpiresAt > DateTime.UtcNow);

        if (resetRequest == null)
            return false;

        try
        {
            switch (resetRequest.ResetType)
            {
                case ResetType.SchwabCredentialsOnly:
                case ResetType.SchwabCredentialsKeepData:
                    await ResetSchwabCredentialsAsync(resetRequest, newCredentials);
                    break;

                case ResetType.FullAccountReset:
                    await ExecuteFullAccountResetAsync(resetRequest, newCredentials);
                    break;

                case ResetType.PasswordOnly:
                    // This should be handled by password reset flow
                    break;
            }

            // Mark request as completed
            resetRequest.IsCompleted = true;
            resetRequest.CompletedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Credential reset completed for user: {UserId}, Type: {ResetType}", resetRequest.UserId, resetRequest.ResetType);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing credential reset for user: {UserId}", resetRequest.UserId);
            return false;
        }
    }

    public async Task<IEnumerable<CredentialResetRequest>> GetPendingResetRequestsAsync(int userId)
    {
        return await _context.CredentialResetRequests
            .Where(r => r.UserId == userId && !r.IsCompleted && r.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    // Account Backup & Recovery Implementation
    public async Task<UserAccountBackup> CreateAccountBackupAsync(int userId, BackupReason reason, string? notes = null)
    {
        var user = await _context.Users
            .Include(u => u.SchwabCredentials)
            .Include(u => u.TradeRecords)
            .Include(u => u.Sessions)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null)
            throw new InvalidOperationException("User not found");

        var backupData = new
        {
            User = new
            {
                user.Username,
                user.Email,
                user.CreatedAt,
                user.LastLoginAt
            },
            SchwabCredentials = user.SchwabCredentials != null ? new
            {
                user.SchwabCredentials.CallbackUrl,
                user.SchwabCredentials.CreatedAt,
                user.SchwabCredentials.UpdatedAt
                // Note: Not backing up encrypted credentials for security
            } : null,
            TradeRecords = user.TradeRecords.Select(t => new
            {
                t.Symbol,
                t.Action,
                t.Quantity,
                t.Price,
                t.TotalValue,
                t.TradingMode,
                t.ExecutedAt,
                t.OrderId,
                t.Notes,
                t.HistoricalDataTimestamp
            }).ToList(),
            BackupMetadata = new
            {
                BackupDate = DateTime.UtcNow,
                Reason = reason,
                TotalTrades = user.TradeRecords.Count,
                SandboxTrades = user.TradeRecords.Count(t => t.TradingMode == TradingMode.Sandbox),
                LiveTrades = user.TradeRecords.Count(t => t.TradingMode == TradingMode.Live)
            }
        };

        var backup = new UserAccountBackup
        {
            UserId = userId,
            BackupReason = reason,
            BackupData = JsonSerializer.Serialize(backupData, new JsonSerializerOptions { WriteIndented = true }),
            Notes = notes,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        };

        _context.UserAccountBackups.Add(backup);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Account backup created for user: {UserId}, Reason: {Reason}", userId, reason);
        return backup;
    }

    public async Task<bool> RestoreAccountFromBackupAsync(int userId, int backupId, bool restoreTradingData = true)
    {
        var backup = await _context.UserAccountBackups
            .FirstOrDefaultAsync(b => b.BackupId == backupId && b.UserId == userId && b.ExpiresAt > DateTime.UtcNow);

        if (backup == null)
            return false;

        try
        {
            var backupData = JsonSerializer.Deserialize<dynamic>(backup.BackupData);

            if (restoreTradingData && backupData != null)
            {
                // Implementation would restore trade records from backup
                // This is a complex operation that would need careful handling
                _logger.LogInformation("Trading data restoration requested for user: {UserId} from backup: {BackupId}", userId, backupId);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring account from backup for user: {UserId}, Backup: {BackupId}", userId, backupId);
            return false;
        }
    }

    public async Task<IEnumerable<UserAccountBackup>> GetAccountBackupsAsync(int userId)
    {
        return await _context.UserAccountBackups
            .Where(b => b.UserId == userId && b.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();
    }

    public async Task CleanupExpiredBackupsAsync()
    {
        var expiredBackups = await _context.UserAccountBackups
            .Where(b => b.ExpiresAt <= DateTime.UtcNow)
            .ToListAsync();

        _context.UserAccountBackups.RemoveRange(expiredBackups);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Cleaned up {Count} expired backups", expiredBackups.Count);
    }

    // Account Management Implementation
    public async Task<bool> DeactivateUserAsync(int userId, string reason)
    {
        var user = await GetUserByIdAsync(userId);
        if (user == null)
            return false;

        // Create backup before deactivation
        await CreateAccountBackupAsync(userId, BackupReason.AccountDeletion, $"Account deactivation: {reason}");

        user.IsActive = false;

        // Invalidate all sessions
        var userSessions = await _context.UserSessions
            .Where(s => s.UserId == userId && s.IsActive)
            .ToListAsync();

        foreach (var session in userSessions)
        {
            session.IsActive = false;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("User deactivated: {UserId}, Reason: {Reason}", userId, reason);
        return true;
    }

    public async Task<bool> ReactivateUserAsync(int userId)
    {
        var user = await GetUserByIdAsync(userId);
        if (user == null)
            return false;

        user.IsActive = true;
        await _context.SaveChangesAsync();

        _logger.LogInformation("User reactivated: {UserId}", userId);
        return true;
    }

    public async Task<AccountStatistics> GetAccountStatisticsAsync(int userId)
    {
        var user = await _context.Users
            .Include(u => u.SchwabCredentials)
            .Include(u => u.TradeRecords)
            .Include(u => u.PasswordResetTokens)
            .Include(u => u.CredentialResetRequests)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null)
            throw new InvalidOperationException("User not found");

        var sandboxTrades = user.TradeRecords.Where(t => t.TradingMode == TradingMode.Sandbox).ToList();
        var liveTrades = user.TradeRecords.Where(t => t.TradingMode == TradingMode.Live).ToList();

        var topSymbols = user.TradeRecords
            .GroupBy(t => t.Symbol)
            .OrderByDescending(g => g.Sum(t => t.TotalValue))
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.TotalValue));

        var currentPositions = await GetCurrentPositionsAsync(userId, TradingMode.Sandbox);

        return new AccountStatistics
        {
            UserId = userId,
            AccountCreated = user.CreatedAt,
            LastLogin = user.LastLoginAt,
            TotalTrades = user.TradeRecords.Count,
            SandboxTrades = sandboxTrades.Count,
            LiveTrades = liveTrades.Count,
            TotalSandboxVolume = sandboxTrades.Sum(t => t.TotalValue),
            TotalLiveVolume = liveTrades.Sum(t => t.TotalValue),
            CurrentSandboxBalance = await GetAccountBalanceAsync(userId, TradingMode.Sandbox),
            CurrentLiveBalance = await GetAccountBalanceAsync(userId, TradingMode.Live),
            ActivePositions = currentPositions.Count,
            TopTradedSymbols = topSymbols,
            HasSchwabCredentials = user.SchwabCredentials != null,
            LastCredentialUpdate = user.SchwabCredentials?.UpdatedAt,
            PasswordResetCount = user.PasswordResetTokens.Count(t => t.IsUsed),
            CredentialResetCount = user.CredentialResetRequests.Count(r => r.IsCompleted)
        };
    }

    // Private helper methods for resets
    private async Task ResetSchwabCredentialsAsync(CredentialResetRequest resetRequest, SchwabCredentialsRequest? newCredentials)
    {
        // Remove existing Schwab credentials
        if (resetRequest.User.SchwabCredentials != null)
        {
            _context.UserCredentials.Remove(resetRequest.User.SchwabCredentials);
        }

        // Add new credentials if provided
        if (newCredentials != null)
        {
            await StoreSchwabCredentialsAsync(resetRequest.UserId, newCredentials.AppKey, newCredentials.AppSecret, newCredentials.CallbackUrl);
        }

        // Invalidate all user sessions to force re-authentication
        var userSessions = await _context.UserSessions
            .Where(s => s.UserId == resetRequest.UserId && s.IsActive)
            .ToListAsync();

        foreach (var session in userSessions)
        {
            session.IsActive = false;
        }
    }

    private async Task ExecuteFullAccountResetAsync(CredentialResetRequest resetRequest, SchwabCredentialsRequest? newCredentials)
    {
        var userId = resetRequest.UserId;

        // Remove Schwab credentials
        if (resetRequest.User.SchwabCredentials != null)
        {
            _context.UserCredentials.Remove(resetRequest.User.SchwabCredentials);
        }

        // Optionally remove trading data based on preservation flags
        if (!resetRequest.PreserveTradingHistory)
        {
            var tradesToRemove = await _context.TradeRecords
                .Where(t => t.UserId == userId)
                .ToListAsync();

            if (!resetRequest.PreserveSandboxData)
            {
                tradesToRemove = tradesToRemove.Where(t => t.TradingMode == TradingMode.Sandbox).ToList();
            }

            if (!resetRequest.PreserveLiveData)
            {
                tradesToRemove.AddRange(tradesToRemove.Where(t => t.TradingMode == TradingMode.Live));
            }

            _context.TradeRecords.RemoveRange(tradesToRemove);
        }

        // Invalidate all sessions
        var userSessions = await _context.UserSessions
            .Where(s => s.UserId == userId && s.IsActive)
            .ToListAsync();

        foreach (var session in userSessions)
        {
            session.IsActive = false;
        }

        // Add new credentials if provided
        if (newCredentials != null)
        {
            await StoreSchwabCredentialsAsync(userId, newCredentials.AppKey, newCredentials.AppSecret, newCredentials.CallbackUrl);
        }
    }

    private static string GenerateSecureToken()
    {
        const int tokenSize = 64;
        using var rng = RandomNumberGenerator.Create();
        var tokenBytes = new byte[tokenSize];
        rng.GetBytes(tokenBytes);
        return Convert.ToBase64String(tokenBytes).Replace("/", "_").Replace("+", "-").Replace("=", "");
    }
}