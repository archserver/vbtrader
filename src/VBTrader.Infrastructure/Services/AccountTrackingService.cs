using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VBTrader.Core.Models;
using VBTrader.Infrastructure.Database;

namespace VBTrader.Infrastructure.Services;

/// <summary>
/// Service for tracking and storing account information and history
/// </summary>
public class AccountTrackingService
{
    private readonly VBTraderDbContext _context;
    private readonly ILogger<AccountTrackingService> _logger;

    public AccountTrackingService(VBTraderDbContext context, ILogger<AccountTrackingService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Save account information from Schwab API response and update history
    /// </summary>
    public async Task SaveAccountInformationAsync(int userId, string rawApiResponse, string changeReason = "API_REFRESH")
    {
        try
        {
            var apiResponseHash = ComputeHash(rawApiResponse);
            var accountData = ParseSchwabAccountResponse(rawApiResponse);

            if (accountData == null || !accountData.Any())
            {
                _logger.LogWarning("No account data found in API response for user {UserId}", userId);
                return;
            }

            foreach (var account in accountData)
            {
                await SaveSingleAccountAsync(userId, account, apiResponseHash, changeReason);
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Successfully saved account information for {AccountCount} accounts for user {UserId}",
                accountData.Count, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving account information for user {UserId}", userId);
            throw;
        }
    }

    private async Task SaveSingleAccountAsync(int userId, SchwabAccountData accountData, string apiResponseHash, string changeReason)
    {
        try
        {
            _logger.LogDebug("Processing account {AccountNumber} for user {UserId}", accountData.AccountNumber, userId);

            // Get existing account information
            var existingAccount = await _context.AccountInformation
                .FirstOrDefaultAsync(a => a.AccountNumber == accountData.AccountNumber && a.UserId == userId);

            var newAccountInfo = CreateAccountInformation(userId, accountData);

            if (existingAccount != null)
            {
                // Check if data has actually changed
                if (!HasAccountDataChanged(existingAccount, newAccountInfo))
                {
                    _logger.LogDebug("No changes detected for account {AccountNumber}", accountData.AccountNumber);
                    return;
                }

                // Calculate changes
                var cashChange = newAccountInfo.CashBalance - existingAccount.CashBalance;
                var liquidationChange = newAccountInfo.LiquidationValue - existingAccount.LiquidationValue;
                var totalValueChange = newAccountInfo.CurrentLiquidationValue - existingAccount.CurrentLiquidationValue;

                _logger.LogDebug("Account {AccountNumber} changes detected - Cash: {CashChange:C}, Liquidation: {LiquidationChange:C}, Total: {TotalChange:C}",
                    accountData.AccountNumber, cashChange, liquidationChange, totalValueChange);

                // Create history record
                var historyRecord = CreateAccountHistory(userId, newAccountInfo, changeReason, apiResponseHash);
                historyRecord.CashChange = cashChange;
                historyRecord.LiquidationValueChange = liquidationChange;
                historyRecord.TotalValueChange = totalValueChange;

                _context.AccountHistory.Add(historyRecord);

                // Update existing account information
                UpdateAccountInformation(existingAccount, newAccountInfo);

                _logger.LogInformation("Account {AccountNumber} updated - Cash: {CashChange:C}, Total: {TotalChange:C}",
                    accountData.AccountNumber, cashChange, totalValueChange);
            }
            else
            {
                _logger.LogDebug("First time processing account {AccountNumber} for user {UserId}", accountData.AccountNumber, userId);

                // First time seeing this account
                _context.AccountInformation.Add(newAccountInfo);

                var historyRecord = CreateAccountHistory(userId, newAccountInfo, "ACCOUNT_DISCOVERED", apiResponseHash);
                _context.AccountHistory.Add(historyRecord);

                _logger.LogInformation("New account discovered: {AccountNumber} with total value {TotalValue:C}",
                    accountData.AccountNumber, newAccountInfo.CurrentLiquidationValue);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing account {AccountNumber} for user {UserId}",
                accountData.AccountNumber, userId);
            throw;
        }
    }

    private List<SchwabAccountData>? ParseSchwabAccountResponse(string rawApiResponse)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(rawApiResponse);
            var accounts = jsonDoc.RootElement.EnumerateArray().ToList();

            return accounts.Select(accountElement =>
            {
                var securitiesAccount = accountElement.GetProperty("securitiesAccount");
                var currentBalances = securitiesAccount.GetProperty("currentBalances");
                var initialBalances = securitiesAccount.GetProperty("initialBalances");
                var aggregatedBalance = accountElement.GetProperty("aggregatedBalance");

                return new SchwabAccountData
                {
                    AccountNumber = securitiesAccount.GetProperty("accountNumber").GetString() ?? "",
                    AccountType = securitiesAccount.GetProperty("type").GetString() ?? "",
                    RoundTrips = securitiesAccount.GetProperty("roundTrips").GetInt32(),
                    IsDayTrader = securitiesAccount.GetProperty("isDayTrader").GetBoolean(),
                    IsClosingOnlyRestricted = securitiesAccount.GetProperty("isClosingOnlyRestricted").GetBoolean(),
                    PfcbFlag = securitiesAccount.GetProperty("pfcbFlag").GetBoolean(),

                    // Current Balances
                    AccruedInterest = GetDecimalProperty(currentBalances, "accruedInterest"),
                    CashAvailableForTrading = GetDecimalProperty(currentBalances, "cashAvailableForTrading"),
                    CashAvailableForWithdrawal = GetDecimalProperty(currentBalances, "cashAvailableForWithdrawal"),
                    CashBalance = GetDecimalProperty(currentBalances, "cashBalance"),
                    BondValue = GetDecimalProperty(currentBalances, "bondValue"),
                    CashReceipts = GetDecimalProperty(currentBalances, "cashReceipts"),
                    LiquidationValue = GetDecimalProperty(currentBalances, "liquidationValue"),
                    LongOptionMarketValue = GetDecimalProperty(currentBalances, "longOptionMarketValue"),
                    LongMarketValue = GetDecimalProperty(currentBalances, "longMarketValue"),
                    MoneyMarketFund = GetDecimalProperty(currentBalances, "moneyMarketFund"),
                    MutualFundValue = GetDecimalProperty(currentBalances, "mutualFundValue"),
                    ShortOptionMarketValue = GetDecimalProperty(currentBalances, "shortOptionMarketValue"),
                    ShortMarketValue = GetDecimalProperty(currentBalances, "shortMarketValue"),
                    Savings = GetDecimalProperty(currentBalances, "savings"),
                    TotalCash = GetDecimalProperty(currentBalances, "totalCash"),
                    UnsettledCash = GetDecimalProperty(currentBalances, "unsettledCash"),
                    CashDebitCallValue = GetDecimalProperty(currentBalances, "cashDebitCallValue"),
                    PendingDeposits = GetDecimalProperty(currentBalances, "pendingDeposits"),
                    CashCall = GetDecimalProperty(currentBalances, "cashCall"),
                    LongNonMarginableMarketValue = GetDecimalProperty(currentBalances, "longNonMarginableMarketValue"),
                    IsInCall = GetBooleanProperty(initialBalances, "isInCall"),

                    // From initial balances
                    LongStockValue = GetDecimalProperty(initialBalances, "longStockValue"),

                    // Aggregated Balance
                    CurrentLiquidationValue = GetDecimalProperty(aggregatedBalance, "currentLiquidationValue"),
                    AggregatedLiquidationValue = GetDecimalProperty(aggregatedBalance, "liquidationValue")
                };
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Schwab account response: {Response}", rawApiResponse);
            return null;
        }
    }

    private decimal GetDecimalProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                var value = prop.GetDecimal();
                _logger.LogTrace("Parsed {PropertyName}: {Value}", propertyName, value);
                return value;
            }
            else
            {
                _logger.LogDebug("Property {PropertyName} found but not a number (ValueKind: {ValueKind})",
                    propertyName, prop.ValueKind);
            }
        }
        else
        {
            _logger.LogTrace("Property {PropertyName} not found, defaulting to 0", propertyName);
        }
        return 0m;
    }

    private bool GetBooleanProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
            {
                var value = prop.GetBoolean();
                _logger.LogTrace("Parsed {PropertyName}: {Value}", propertyName, value);
                return value;
            }
            else
            {
                _logger.LogDebug("Property {PropertyName} found but not a boolean (ValueKind: {ValueKind})",
                    propertyName, prop.ValueKind);
            }
        }
        else
        {
            _logger.LogTrace("Property {PropertyName} not found, defaulting to false", propertyName);
        }
        return false;
    }

    private AccountInformation CreateAccountInformation(int userId, SchwabAccountData data)
    {
        return new AccountInformation
        {
            UserId = userId,
            AccountNumber = data.AccountNumber,
            AccountType = data.AccountType,
            RoundTrips = data.RoundTrips,
            IsDayTrader = data.IsDayTrader,
            IsClosingOnlyRestricted = data.IsClosingOnlyRestricted,
            PfcbFlag = data.PfcbFlag,
            AccruedInterest = data.AccruedInterest,
            CashAvailableForTrading = data.CashAvailableForTrading,
            CashAvailableForWithdrawal = data.CashAvailableForWithdrawal,
            CashBalance = data.CashBalance,
            BondValue = data.BondValue,
            CashReceipts = data.CashReceipts,
            LiquidationValue = data.LiquidationValue,
            LongOptionMarketValue = data.LongOptionMarketValue,
            LongStockValue = data.LongStockValue,
            LongMarketValue = data.LongMarketValue,
            MoneyMarketFund = data.MoneyMarketFund,
            MutualFundValue = data.MutualFundValue,
            ShortOptionMarketValue = data.ShortOptionMarketValue,
            ShortStockValue = data.ShortStockValue,
            ShortMarketValue = data.ShortMarketValue,
            Savings = data.Savings,
            TotalCash = data.TotalCash,
            UnsettledCash = data.UnsettledCash,
            CashDebitCallValue = data.CashDebitCallValue,
            PendingDeposits = data.PendingDeposits,
            CashCall = data.CashCall,
            LongNonMarginableMarketValue = data.LongNonMarginableMarketValue,
            IsInCall = data.IsInCall,
            CurrentLiquidationValue = data.CurrentLiquidationValue,
            AggregatedLiquidationValue = data.AggregatedLiquidationValue,
            LastUpdated = DateTime.UtcNow
        };
    }

    private AccountHistory CreateAccountHistory(int userId, AccountInformation accountInfo, string changeReason, string apiResponseHash)
    {
        return new AccountHistory
        {
            UserId = userId,
            AccountNumber = accountInfo.AccountNumber,
            AccountType = accountInfo.AccountType,
            RoundTrips = accountInfo.RoundTrips,
            IsDayTrader = accountInfo.IsDayTrader,
            IsClosingOnlyRestricted = accountInfo.IsClosingOnlyRestricted,
            PfcbFlag = accountInfo.PfcbFlag,
            AccruedInterest = accountInfo.AccruedInterest,
            CashAvailableForTrading = accountInfo.CashAvailableForTrading,
            CashAvailableForWithdrawal = accountInfo.CashAvailableForWithdrawal,
            CashBalance = accountInfo.CashBalance,
            BondValue = accountInfo.BondValue,
            CashReceipts = accountInfo.CashReceipts,
            LiquidationValue = accountInfo.LiquidationValue,
            LongOptionMarketValue = accountInfo.LongOptionMarketValue,
            LongStockValue = accountInfo.LongStockValue,
            LongMarketValue = accountInfo.LongMarketValue,
            MoneyMarketFund = accountInfo.MoneyMarketFund,
            MutualFundValue = accountInfo.MutualFundValue,
            ShortOptionMarketValue = accountInfo.ShortOptionMarketValue,
            ShortStockValue = accountInfo.ShortStockValue,
            ShortMarketValue = accountInfo.ShortMarketValue,
            Savings = accountInfo.Savings,
            TotalCash = accountInfo.TotalCash,
            UnsettledCash = accountInfo.UnsettledCash,
            CashDebitCallValue = accountInfo.CashDebitCallValue,
            PendingDeposits = accountInfo.PendingDeposits,
            CashCall = accountInfo.CashCall,
            LongNonMarginableMarketValue = accountInfo.LongNonMarginableMarketValue,
            IsInCall = accountInfo.IsInCall,
            CurrentLiquidationValue = accountInfo.CurrentLiquidationValue,
            AggregatedLiquidationValue = accountInfo.AggregatedLiquidationValue,
            ChangeReason = changeReason,
            ApiResponseHash = apiResponseHash,
            SnapshotTime = DateTime.UtcNow
        };
    }

    private void UpdateAccountInformation(AccountInformation existing, AccountInformation updated)
    {
        existing.AccountType = updated.AccountType;
        existing.RoundTrips = updated.RoundTrips;
        existing.IsDayTrader = updated.IsDayTrader;
        existing.IsClosingOnlyRestricted = updated.IsClosingOnlyRestricted;
        existing.PfcbFlag = updated.PfcbFlag;
        existing.AccruedInterest = updated.AccruedInterest;
        existing.CashAvailableForTrading = updated.CashAvailableForTrading;
        existing.CashAvailableForWithdrawal = updated.CashAvailableForWithdrawal;
        existing.CashBalance = updated.CashBalance;
        existing.BondValue = updated.BondValue;
        existing.CashReceipts = updated.CashReceipts;
        existing.LiquidationValue = updated.LiquidationValue;
        existing.LongOptionMarketValue = updated.LongOptionMarketValue;
        existing.LongStockValue = updated.LongStockValue;
        existing.LongMarketValue = updated.LongMarketValue;
        existing.MoneyMarketFund = updated.MoneyMarketFund;
        existing.MutualFundValue = updated.MutualFundValue;
        existing.ShortOptionMarketValue = updated.ShortOptionMarketValue;
        existing.ShortStockValue = updated.ShortStockValue;
        existing.ShortMarketValue = updated.ShortMarketValue;
        existing.Savings = updated.Savings;
        existing.TotalCash = updated.TotalCash;
        existing.UnsettledCash = updated.UnsettledCash;
        existing.CashDebitCallValue = updated.CashDebitCallValue;
        existing.PendingDeposits = updated.PendingDeposits;
        existing.CashCall = updated.CashCall;
        existing.LongNonMarginableMarketValue = updated.LongNonMarginableMarketValue;
        existing.IsInCall = updated.IsInCall;
        existing.CurrentLiquidationValue = updated.CurrentLiquidationValue;
        existing.AggregatedLiquidationValue = updated.AggregatedLiquidationValue;
        existing.LastUpdated = DateTime.UtcNow;
    }

    private bool HasAccountDataChanged(AccountInformation existing, AccountInformation updated)
    {
        _logger.LogTrace("Checking for account data changes for account {AccountNumber}", existing.AccountNumber);

        var changes = new List<string>();

        if (existing.CashBalance != updated.CashBalance)
            changes.Add($"CashBalance: {existing.CashBalance:C} → {updated.CashBalance:C}");

        if (existing.LiquidationValue != updated.LiquidationValue)
            changes.Add($"LiquidationValue: {existing.LiquidationValue:C} → {updated.LiquidationValue:C}");

        if (existing.CurrentLiquidationValue != updated.CurrentLiquidationValue)
            changes.Add($"CurrentLiquidationValue: {existing.CurrentLiquidationValue:C} → {updated.CurrentLiquidationValue:C}");

        if (existing.CashAvailableForTrading != updated.CashAvailableForTrading)
            changes.Add($"CashAvailableForTrading: {existing.CashAvailableForTrading:C} → {updated.CashAvailableForTrading:C}");

        if (existing.LongMarketValue != updated.LongMarketValue)
            changes.Add($"LongMarketValue: {existing.LongMarketValue:C} → {updated.LongMarketValue:C}");

        if (existing.TotalCash != updated.TotalCash)
            changes.Add($"TotalCash: {existing.TotalCash:C} → {updated.TotalCash:C}");

        if (existing.RoundTrips != updated.RoundTrips)
            changes.Add($"RoundTrips: {existing.RoundTrips} → {updated.RoundTrips}");

        if (existing.IsDayTrader != updated.IsDayTrader)
            changes.Add($"IsDayTrader: {existing.IsDayTrader} → {updated.IsDayTrader}");

        if (changes.Any())
        {
            _logger.LogDebug("Account data changes detected for {AccountNumber}: {Changes}",
                existing.AccountNumber, string.Join(", ", changes));
            return true;
        }
        else
        {
            _logger.LogTrace("No meaningful changes detected for account {AccountNumber}", existing.AccountNumber);
            return false;
        }
    }

    private string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Get account history for a specific account
    /// </summary>
    public async Task<List<AccountHistory>> GetAccountHistoryAsync(string accountNumber, int userId, DateTime? fromDate = null, int limit = 100)
    {
        try
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(accountNumber))
            {
                _logger.LogError("GetAccountHistoryAsync called with null or empty accountNumber");
                throw new ArgumentException("Account number cannot be null or empty", nameof(accountNumber));
            }

            if (userId <= 0)
            {
                _logger.LogError("GetAccountHistoryAsync called with invalid userId: {UserId}", userId);
                throw new ArgumentException("User ID must be greater than 0", nameof(userId));
            }

            if (limit <= 0 || limit > 1000)
            {
                _logger.LogError("GetAccountHistoryAsync called with invalid limit: {Limit}", limit);
                throw new ArgumentException("Limit must be between 1 and 1000", nameof(limit));
            }

            _logger.LogDebug("Getting account history for account {AccountNumber}, user {UserId}, fromDate {FromDate}, limit {Limit}",
                accountNumber, userId, fromDate, limit);

            var query = _context.AccountHistory
                .Where(h => h.AccountNumber == accountNumber && h.UserId == userId);

            if (fromDate.HasValue)
            {
                query = query.Where(h => h.SnapshotTime >= fromDate);
                _logger.LogDebug("Filtering history from date: {FromDate}", fromDate.Value);
            }

            var results = await query
                .OrderByDescending(h => h.SnapshotTime)
                .Take(limit)
                .ToListAsync();

            _logger.LogInformation("Retrieved {Count} account history records for account {AccountNumber}",
                results.Count, accountNumber);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving account history for account {AccountNumber}, user {UserId}",
                accountNumber, userId);
            throw;
        }
    }

    /// <summary>
    /// Get current account information
    /// </summary>
    public async Task<AccountInformation?> GetCurrentAccountInformationAsync(string accountNumber, int userId)
    {
        try
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(accountNumber))
            {
                _logger.LogError("GetCurrentAccountInformationAsync called with null or empty accountNumber");
                throw new ArgumentException("Account number cannot be null or empty", nameof(accountNumber));
            }

            if (userId <= 0)
            {
                _logger.LogError("GetCurrentAccountInformationAsync called with invalid userId: {UserId}", userId);
                throw new ArgumentException("User ID must be greater than 0", nameof(userId));
            }

            _logger.LogDebug("Getting current account information for account {AccountNumber}, user {UserId}",
                accountNumber, userId);

            var result = await _context.AccountInformation
                .FirstOrDefaultAsync(a => a.AccountNumber == accountNumber && a.UserId == userId);

            if (result != null)
            {
                _logger.LogInformation("Found current account information for account {AccountNumber}, last updated {LastUpdated}",
                    accountNumber, result.LastUpdated);
            }
            else
            {
                _logger.LogWarning("No current account information found for account {AccountNumber}, user {UserId}",
                    accountNumber, userId);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current account information for account {AccountNumber}, user {UserId}",
                accountNumber, userId);
            throw;
        }
    }

    private class SchwabAccountData
    {
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public int RoundTrips { get; set; }
        public bool IsDayTrader { get; set; }
        public bool IsClosingOnlyRestricted { get; set; }
        public bool PfcbFlag { get; set; }
        public decimal AccruedInterest { get; set; }
        public decimal CashAvailableForTrading { get; set; }
        public decimal CashAvailableForWithdrawal { get; set; }
        public decimal CashBalance { get; set; }
        public decimal BondValue { get; set; }
        public decimal CashReceipts { get; set; }
        public decimal LiquidationValue { get; set; }
        public decimal LongOptionMarketValue { get; set; }
        public decimal LongStockValue { get; set; }
        public decimal LongMarketValue { get; set; }
        public decimal MoneyMarketFund { get; set; }
        public decimal MutualFundValue { get; set; }
        public decimal ShortOptionMarketValue { get; set; }
        public decimal ShortStockValue { get; set; }
        public decimal ShortMarketValue { get; set; }
        public decimal Savings { get; set; }
        public decimal TotalCash { get; set; }
        public decimal UnsettledCash { get; set; }
        public decimal CashDebitCallValue { get; set; }
        public decimal PendingDeposits { get; set; }
        public decimal CashCall { get; set; }
        public decimal LongNonMarginableMarketValue { get; set; }
        public bool IsInCall { get; set; }
        public decimal CurrentLiquidationValue { get; set; }
        public decimal AggregatedLiquidationValue { get; set; }
    }
}