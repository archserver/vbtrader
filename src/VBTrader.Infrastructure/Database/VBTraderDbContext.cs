using Microsoft.EntityFrameworkCore;
using VBTrader.Infrastructure.Database.Entities;
using VBTrader.Core.Models;

namespace VBTrader.Infrastructure.Database;

public class VBTraderDbContext : DbContext
{
    public VBTraderDbContext(DbContextOptions<VBTraderDbContext> options) : base(options)
    {
    }

    public DbSet<StockQuoteEntity> StockQuotes { get; set; }
    public DbSet<CandlestickDataEntity> CandlestickData { get; set; }
    public DbSet<MarketOpportunityEntity> MarketOpportunities { get; set; }
    public DbSet<TradingSessionEntity> TradingSessions { get; set; }

    // User and authentication tables
    public DbSet<User> Users { get; set; }
    public DbSet<UserCredentials> UserCredentials { get; set; }
    public DbSet<UserSession> UserSessions { get; set; }
    public DbSet<TradeRecord> TradeRecords { get; set; }
    public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
    public DbSet<CredentialResetRequest> CredentialResetRequests { get; set; }
    public DbSet<UserAccountBackup> UserAccountBackups { get; set; }

    // Sandbox trading tables
    public DbSet<SandboxSessionEntity> SandboxSessions { get; set; }
    public DbSet<SandboxSettingsEntity> SandboxSettings { get; set; }
    public DbSet<SandboxTradeEntity> SandboxTrades { get; set; }
    public DbSet<HistoricalDataCacheEntity> HistoricalDataCache { get; set; }
    public DbSet<SandboxPerformanceSnapshotEntity> SandboxPerformanceSnapshots { get; set; }

    // Account information tracking tables
    public DbSet<AccountInformation> AccountInformation { get; set; }
    public DbSet<AccountHistory> AccountHistory { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure StockQuoteEntity
        modelBuilder.Entity<StockQuoteEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Symbol, e.Timestamp })
                .HasDatabaseName("IX_StockQuotes_Symbol_Timestamp");
            entity.HasIndex(e => e.Timestamp)
                .HasDatabaseName("IX_StockQuotes_Timestamp");
            entity.HasIndex(e => new { e.IsPreMarket, e.Timestamp })
                .HasDatabaseName("IX_StockQuotes_IsPreMarket_Timestamp");

            entity.Property(e => e.Symbol)
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(e => e.LastPrice)
                .HasPrecision(18, 6);
            entity.Property(e => e.Change)
                .HasPrecision(18, 6);
            entity.Property(e => e.ChangePercent)
                .HasPrecision(18, 6);
            entity.Property(e => e.Ask)
                .HasPrecision(18, 6);
            entity.Property(e => e.Bid)
                .HasPrecision(18, 6);
            entity.Property(e => e.High)
                .HasPrecision(18, 6);
            entity.Property(e => e.Low)
                .HasPrecision(18, 6);
            entity.Property(e => e.Open)
                .HasPrecision(18, 6);
            entity.Property(e => e.PreviousClose)
                .HasPrecision(18, 6);
            entity.Property(e => e.MarketCap)
                .HasPrecision(18, 2);
            entity.Property(e => e.PreMarketChangePercent)
                .HasPrecision(18, 6);

            entity.Property(e => e.NewsHeadline)
                .HasMaxLength(500);

            // Table partitioning by date (PostgreSQL specific)
            entity.ToTable("stock_quotes");
        });

        // Configure CandlestickDataEntity
        modelBuilder.Entity<CandlestickDataEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Enhanced indexes for new timestamp and timeframe fields
            entity.HasIndex(e => new { e.Symbol, e.MarketTimestamp })
                .HasDatabaseName("IX_CandlestickData_Symbol_MarketTimestamp");
            entity.HasIndex(e => new { e.Symbol, e.TimeFrameType, e.TimeFrameValue, e.MarketTimestamp })
                .HasDatabaseName("IX_CandlestickData_Symbol_TimeFrame_MarketTimestamp");
            entity.HasIndex(e => e.MarketTimestamp)
                .HasDatabaseName("IX_CandlestickData_MarketTimestamp");
            entity.HasIndex(e => e.FetchedAt)
                .HasDatabaseName("IX_CandlestickData_FetchedAt");
            entity.HasIndex(e => new { e.DataType, e.IsRealTime })
                .HasDatabaseName("IX_CandlestickData_DataType_IsRealTime");
            entity.HasIndex(e => e.DataSource)
                .HasDatabaseName("IX_CandlestickData_DataSource");

            // Legacy indexes for backward compatibility
            entity.HasIndex(e => new { e.Symbol, e.Timestamp })
                .HasDatabaseName("IX_CandlestickData_Symbol_Timestamp_Legacy");
            entity.HasIndex(e => e.Timestamp)
                .HasDatabaseName("IX_CandlestickData_Timestamp_Legacy");

            // Basic properties
            entity.Property(e => e.Symbol)
                .IsRequired()
                .HasMaxLength(10);

            // Enhanced timestamp properties
            entity.Property(e => e.MarketTimestamp)
                .IsRequired();
            entity.Property(e => e.MarketTimestampMs)
                .IsRequired();
            entity.Property(e => e.FetchedAt)
                .IsRequired();

            // TimeFrame properties
            entity.Property(e => e.TimeFrameType)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("minute");
            entity.Property(e => e.TimeFrameValue)
                .IsRequired()
                .HasDefaultValue(1);

            // Data classification properties
            entity.Property(e => e.DataType)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("ohlc");
            entity.Property(e => e.DataSource)
                .HasMaxLength(50)
                .HasDefaultValue("Schwab");

            // OHLCV with enhanced precision
            entity.Property(e => e.Open)
                .HasPrecision(18, 6);
            entity.Property(e => e.High)
                .HasPrecision(18, 6);
            entity.Property(e => e.Low)
                .HasPrecision(18, 6);
            entity.Property(e => e.Close)
                .HasPrecision(18, 6);
            entity.Property(e => e.PreviousClose)
                .HasPrecision(18, 6);

            // Technical indicators with enhanced precision
            entity.Property(e => e.MACD)
                .HasPrecision(18, 6);
            entity.Property(e => e.MACDSignal)
                .HasPrecision(18, 6);
            entity.Property(e => e.MACDHistogram)
                .HasPrecision(18, 6);
            entity.Property(e => e.EMA12)
                .HasPrecision(18, 6);
            entity.Property(e => e.EMA26)
                .HasPrecision(18, 6);
            entity.Property(e => e.RSI)
                .HasPrecision(18, 6);
            entity.Property(e => e.BollingerUpper)
                .HasPrecision(18, 6);
            entity.Property(e => e.BollingerLower)
                .HasPrecision(18, 6);
            entity.Property(e => e.BollingerMiddle)
                .HasPrecision(18, 6);

            entity.ToTable("candlestick_data");
        });

        // Configure MarketOpportunityEntity
        modelBuilder.Entity<MarketOpportunityEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Symbol, e.Timestamp })
                .HasDatabaseName("IX_MarketOpportunities_Symbol_Timestamp");
            entity.HasIndex(e => e.Timestamp)
                .HasDatabaseName("IX_MarketOpportunities_Timestamp");
            entity.HasIndex(e => e.Score)
                .HasDatabaseName("IX_MarketOpportunities_Score");

            entity.Property(e => e.Symbol)
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(e => e.Score)
                .HasPrecision(18, 6);
            entity.Property(e => e.VolumeChange)
                .HasPrecision(18, 6);
            entity.Property(e => e.PriceChangePercent)
                .HasPrecision(18, 6);
            entity.Property(e => e.Confidence)
                .HasPrecision(18, 6);

            entity.Property(e => e.Reason)
                .HasMaxLength(1000);

            entity.ToTable("market_opportunities");
        });

        // Configure TradingSessionEntity
        modelBuilder.Entity<TradingSessionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionDate)
                .HasDatabaseName("IX_TradingSessions_SessionDate");

            entity.ToTable("trading_sessions");
        });

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.HasIndex(e => e.Username)
                .IsUnique()
                .HasDatabaseName("IX_Users_Username");
            entity.HasIndex(e => e.Email)
                .HasDatabaseName("IX_Users_Email");

            entity.Property(e => e.Username)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.PasswordHash)
                .IsRequired()
                .HasMaxLength(255);
            entity.Property(e => e.Salt)
                .IsRequired()
                .HasMaxLength(50);
            entity.Property(e => e.Email)
                .IsRequired()
                .HasMaxLength(100);

            entity.ToTable("users");
        });

        // Configure UserCredentials entity
        modelBuilder.Entity<UserCredentials>(entity =>
        {
            entity.HasKey(e => e.CredentialsId);
            entity.HasIndex(e => e.UserId)
                .IsUnique()
                .HasDatabaseName("IX_UserCredentials_UserId");

            entity.Property(e => e.EncryptedSchwabAppKey)
                .IsRequired()
                .HasMaxLength(500);
            entity.Property(e => e.EncryptedSchwabAppSecret)
                .IsRequired()
                .HasMaxLength(500);
            entity.Property(e => e.CallbackUrl)
                .IsRequired()
                .HasMaxLength(200);
            entity.Property(e => e.EncryptionSalt)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasOne(e => e.User)
                .WithOne(u => u.SchwabCredentials)
                .HasForeignKey<UserCredentials>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("user_credentials");
        });

        // Configure UserSession entity
        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.HasIndex(e => e.SessionToken)
                .IsUnique()
                .HasDatabaseName("IX_UserSessions_SessionToken");
            entity.HasIndex(e => new { e.UserId, e.IsActive })
                .HasDatabaseName("IX_UserSessions_UserId_IsActive");

            entity.Property(e => e.SessionToken)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasOne(e => e.User)
                .WithMany(u => u.Sessions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("user_sessions");
        });

        // Configure TradeRecord entity
        modelBuilder.Entity<TradeRecord>(entity =>
        {
            entity.HasKey(e => e.TradeId);
            entity.HasIndex(e => new { e.UserId, e.ExecutedAt })
                .HasDatabaseName("IX_TradeRecords_UserId_ExecutedAt");
            entity.HasIndex(e => new { e.Symbol, e.ExecutedAt })
                .HasDatabaseName("IX_TradeRecords_Symbol_ExecutedAt");
            entity.HasIndex(e => new { e.TradingMode, e.ExecutedAt })
                .HasDatabaseName("IX_TradeRecords_TradingMode_ExecutedAt");

            entity.Property(e => e.Symbol)
                .IsRequired()
                .HasMaxLength(10);
            entity.Property(e => e.Price)
                .HasPrecision(18, 4);
            entity.Property(e => e.TotalValue)
                .HasPrecision(18, 4);
            entity.Property(e => e.OrderId)
                .HasMaxLength(100);
            entity.Property(e => e.Notes)
                .HasMaxLength(500);

            entity.HasOne(e => e.User)
                .WithMany(u => u.TradeRecords)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("trade_records");
        });

        // Configure PasswordResetToken entity
        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.TokenId);
            entity.HasIndex(e => e.Token)
                .IsUnique()
                .HasDatabaseName("IX_PasswordResetTokens_Token");
            entity.HasIndex(e => new { e.UserId, e.IsUsed })
                .HasDatabaseName("IX_PasswordResetTokens_UserId_IsUsed");
            entity.HasIndex(e => e.ExpiresAt)
                .HasDatabaseName("IX_PasswordResetTokens_ExpiresAt");

            entity.Property(e => e.Token)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.Email)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.RequestIpAddress)
                .HasMaxLength(45);

            entity.HasOne(e => e.User)
                .WithMany(u => u.PasswordResetTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("password_reset_tokens");
        });

        // Configure CredentialResetRequest entity
        modelBuilder.Entity<CredentialResetRequest>(entity =>
        {
            entity.HasKey(e => e.RequestId);
            entity.HasIndex(e => e.ResetToken)
                .IsUnique()
                .HasDatabaseName("IX_CredentialResetRequests_ResetToken");
            entity.HasIndex(e => new { e.UserId, e.IsCompleted })
                .HasDatabaseName("IX_CredentialResetRequests_UserId_IsCompleted");
            entity.HasIndex(e => e.ExpiresAt)
                .HasDatabaseName("IX_CredentialResetRequests_ExpiresAt");

            entity.Property(e => e.ResetToken)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.Email)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.ResetReason)
                .HasMaxLength(500);
            entity.Property(e => e.RequestIpAddress)
                .HasMaxLength(45);

            entity.HasOne(e => e.User)
                .WithMany(u => u.CredentialResetRequests)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("credential_reset_requests");
        });

        // Configure UserAccountBackup entity
        modelBuilder.Entity<UserAccountBackup>(entity =>
        {
            entity.HasKey(e => e.BackupId);
            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("IX_UserAccountBackups_UserId");
            entity.HasIndex(e => e.ExpiresAt)
                .HasDatabaseName("IX_UserAccountBackups_ExpiresAt");
            entity.HasIndex(e => new { e.BackupReason, e.CreatedAt })
                .HasDatabaseName("IX_UserAccountBackups_BackupReason_CreatedAt");

            entity.Property(e => e.BackupData)
                .IsRequired();
            entity.Property(e => e.Notes)
                .HasMaxLength(500);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("user_account_backups");
        });

        // Configure SandboxSessionEntity
        modelBuilder.Entity<SandboxSessionEntity>(entity =>
        {
            entity.HasKey(e => e.SandboxSessionId);
            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("IX_SandboxSessions_UserId");
            entity.HasIndex(e => e.IsActive)
                .HasDatabaseName("IX_SandboxSessions_IsActive");
            entity.HasIndex(e => new { e.UserId, e.IsActive })
                .HasDatabaseName("IX_SandboxSessions_UserId_IsActive");

            entity.Property(e => e.SessionName)
                .IsRequired()
                .HasMaxLength(200);
            entity.Property(e => e.InitialBalance)
                .HasPrecision(18, 2);
            entity.Property(e => e.CurrentBalance)
                .HasPrecision(18, 2);
            entity.Property(e => e.WatchedSymbols)
                .HasMaxLength(1000);

            entity.ToTable("sandbox_sessions");
        });

        // Configure SandboxSettingsEntity
        modelBuilder.Entity<SandboxSettingsEntity>(entity =>
        {
            entity.HasKey(e => e.SettingsId);
            entity.HasIndex(e => e.SandboxSessionId)
                .IsUnique()
                .HasDatabaseName("IX_SandboxSettings_SandboxSessionId");

            entity.Property(e => e.SlippagePercentage)
                .HasPrecision(5, 2);
            entity.Property(e => e.CommissionPerTrade)
                .HasPrecision(10, 2);

            entity.HasOne(e => e.SandboxSession)
                .WithOne(s => s.Settings)
                .HasForeignKey<SandboxSettingsEntity>(e => e.SandboxSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("sandbox_settings");
        });

        // Configure SandboxTradeEntity
        modelBuilder.Entity<SandboxTradeEntity>(entity =>
        {
            entity.HasKey(e => e.TradeId);
            entity.HasIndex(e => e.SandboxSessionId)
                .HasDatabaseName("IX_SandboxTrades_SandboxSessionId");
            entity.HasIndex(e => new { e.Symbol, e.ExecutedAt })
                .HasDatabaseName("IX_SandboxTrades_Symbol_ExecutedAt");
            entity.HasIndex(e => e.ExecutedAt)
                .HasDatabaseName("IX_SandboxTrades_ExecutedAt");

            entity.Property(e => e.Symbol)
                .IsRequired()
                .HasMaxLength(10);
            entity.Property(e => e.Price)
                .HasPrecision(18, 4);
            entity.Property(e => e.TotalValue)
                .HasPrecision(18, 4);
            entity.Property(e => e.Commission)
                .HasPrecision(10, 2);
            entity.Property(e => e.LimitPrice)
                .HasPrecision(18, 4);
            entity.Property(e => e.Notes)
                .HasMaxLength(500);

            entity.HasOne(e => e.SandboxSession)
                .WithMany(s => s.Trades)
                .HasForeignKey(e => e.SandboxSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("sandbox_trades");
        });

        // Configure HistoricalDataCacheEntity
        modelBuilder.Entity<HistoricalDataCacheEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Symbol, e.Timestamp })
                .HasDatabaseName("IX_HistoricalDataCache_Symbol_Timestamp");
            entity.HasIndex(e => e.ExpiresAt)
                .HasDatabaseName("IX_HistoricalDataCache_ExpiresAt");
            entity.HasIndex(e => new { e.Symbol, e.TimeFrame, e.Timestamp })
                .HasDatabaseName("IX_HistoricalDataCache_Symbol_TimeFrame_Timestamp");

            entity.Property(e => e.Symbol)
                .IsRequired()
                .HasMaxLength(10);
            entity.Property(e => e.Open)
                .HasPrecision(18, 4);
            entity.Property(e => e.High)
                .HasPrecision(18, 4);
            entity.Property(e => e.Low)
                .HasPrecision(18, 4);
            entity.Property(e => e.Close)
                .HasPrecision(18, 4);
            entity.Property(e => e.AdjustedClose)
                .HasPrecision(18, 4);
            entity.Property(e => e.DataSource)
                .HasMaxLength(50);
            entity.Property(e => e.TimeFrame)
                .HasMaxLength(20);

            entity.ToTable("historical_data_cache");
        });

        // Configure SandboxPerformanceSnapshotEntity
        modelBuilder.Entity<SandboxPerformanceSnapshotEntity>(entity =>
        {
            entity.HasKey(e => e.SnapshotId);
            entity.HasIndex(e => e.SandboxSessionId)
                .HasDatabaseName("IX_SandboxPerformanceSnapshots_SandboxSessionId");
            entity.HasIndex(e => new { e.SandboxSessionId, e.SnapshotTime })
                .HasDatabaseName("IX_SandboxPerformanceSnapshots_SandboxSessionId_SnapshotTime");

            entity.Property(e => e.AccountBalance)
                .HasPrecision(18, 2);
            entity.Property(e => e.PortfolioValue)
                .HasPrecision(18, 2);
            entity.Property(e => e.TotalValue)
                .HasPrecision(18, 2);
            entity.Property(e => e.DayChange)
                .HasPrecision(18, 2);
            entity.Property(e => e.DayChangePercent)
                .HasPrecision(10, 4);
            entity.Property(e => e.UnrealizedPnL)
                .HasPrecision(18, 2);
            entity.Property(e => e.RealizedPnL)
                .HasPrecision(18, 2);
            entity.Property(e => e.PositionsSnapshot)
                .HasMaxLength(2000);

            entity.HasOne(e => e.SandboxSession)
                .WithMany()
                .HasForeignKey(e => e.SandboxSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("sandbox_performance_snapshots");
        });

        // Configure AccountInformation entity
        modelBuilder.Entity<AccountInformation>(entity =>
        {
            entity.HasKey(e => e.AccountInfoId);
            entity.HasIndex(e => new { e.AccountNumber, e.UserId })
                .IsUnique()
                .HasDatabaseName("IX_AccountInformation_AccountNumber_UserId");
            entity.HasIndex(e => e.LastUpdated)
                .HasDatabaseName("IX_AccountInformation_LastUpdated");

            entity.Property(e => e.AccountNumber)
                .IsRequired()
                .HasMaxLength(50);
            entity.Property(e => e.AccountType)
                .HasMaxLength(20);

            // Configure decimal properties with precision
            entity.Property(e => e.AccruedInterest).HasPrecision(18, 2);
            entity.Property(e => e.CashAvailableForTrading).HasPrecision(18, 2);
            entity.Property(e => e.CashAvailableForWithdrawal).HasPrecision(18, 2);
            entity.Property(e => e.CashBalance).HasPrecision(18, 2);
            entity.Property(e => e.BondValue).HasPrecision(18, 2);
            entity.Property(e => e.CashReceipts).HasPrecision(18, 2);
            entity.Property(e => e.LiquidationValue).HasPrecision(18, 2);
            entity.Property(e => e.LongOptionMarketValue).HasPrecision(18, 2);
            entity.Property(e => e.LongStockValue).HasPrecision(18, 2);
            entity.Property(e => e.LongMarketValue).HasPrecision(18, 2);
            entity.Property(e => e.MoneyMarketFund).HasPrecision(18, 2);
            entity.Property(e => e.MutualFundValue).HasPrecision(18, 2);
            entity.Property(e => e.ShortOptionMarketValue).HasPrecision(18, 2);
            entity.Property(e => e.ShortStockValue).HasPrecision(18, 2);
            entity.Property(e => e.ShortMarketValue).HasPrecision(18, 2);
            entity.Property(e => e.Savings).HasPrecision(18, 2);
            entity.Property(e => e.TotalCash).HasPrecision(18, 2);
            entity.Property(e => e.UnsettledCash).HasPrecision(18, 2);
            entity.Property(e => e.CashDebitCallValue).HasPrecision(18, 2);
            entity.Property(e => e.PendingDeposits).HasPrecision(18, 2);
            entity.Property(e => e.CashCall).HasPrecision(18, 2);
            entity.Property(e => e.LongNonMarginableMarketValue).HasPrecision(18, 2);
            entity.Property(e => e.CurrentLiquidationValue).HasPrecision(18, 2);
            entity.Property(e => e.AggregatedLiquidationValue).HasPrecision(18, 2);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("account_information");
        });

        // Configure AccountHistory entity
        modelBuilder.Entity<AccountHistory>(entity =>
        {
            entity.HasKey(e => e.HistoryId);
            entity.HasIndex(e => new { e.AccountNumber, e.SnapshotTime })
                .HasDatabaseName("IX_AccountHistory_AccountNumber_SnapshotTime");
            entity.HasIndex(e => new { e.UserId, e.SnapshotTime })
                .HasDatabaseName("IX_AccountHistory_UserId_SnapshotTime");
            entity.HasIndex(e => e.SnapshotTime)
                .HasDatabaseName("IX_AccountHistory_SnapshotTime");
            entity.HasIndex(e => e.ChangeReason)
                .HasDatabaseName("IX_AccountHistory_ChangeReason");

            entity.Property(e => e.AccountNumber)
                .IsRequired()
                .HasMaxLength(50);
            entity.Property(e => e.AccountType)
                .HasMaxLength(20);
            entity.Property(e => e.ChangeReason)
                .HasMaxLength(100);
            entity.Property(e => e.ApiResponseHash)
                .HasMaxLength(500);

            // Configure decimal properties with precision
            entity.Property(e => e.AccruedInterest).HasPrecision(18, 2);
            entity.Property(e => e.CashAvailableForTrading).HasPrecision(18, 2);
            entity.Property(e => e.CashAvailableForWithdrawal).HasPrecision(18, 2);
            entity.Property(e => e.CashBalance).HasPrecision(18, 2);
            entity.Property(e => e.BondValue).HasPrecision(18, 2);
            entity.Property(e => e.CashReceipts).HasPrecision(18, 2);
            entity.Property(e => e.LiquidationValue).HasPrecision(18, 2);
            entity.Property(e => e.LongOptionMarketValue).HasPrecision(18, 2);
            entity.Property(e => e.LongStockValue).HasPrecision(18, 2);
            entity.Property(e => e.LongMarketValue).HasPrecision(18, 2);
            entity.Property(e => e.MoneyMarketFund).HasPrecision(18, 2);
            entity.Property(e => e.MutualFundValue).HasPrecision(18, 2);
            entity.Property(e => e.ShortOptionMarketValue).HasPrecision(18, 2);
            entity.Property(e => e.ShortStockValue).HasPrecision(18, 2);
            entity.Property(e => e.ShortMarketValue).HasPrecision(18, 2);
            entity.Property(e => e.Savings).HasPrecision(18, 2);
            entity.Property(e => e.TotalCash).HasPrecision(18, 2);
            entity.Property(e => e.UnsettledCash).HasPrecision(18, 2);
            entity.Property(e => e.CashDebitCallValue).HasPrecision(18, 2);
            entity.Property(e => e.PendingDeposits).HasPrecision(18, 2);
            entity.Property(e => e.CashCall).HasPrecision(18, 2);
            entity.Property(e => e.LongNonMarginableMarketValue).HasPrecision(18, 2);
            entity.Property(e => e.CurrentLiquidationValue).HasPrecision(18, 2);
            entity.Property(e => e.AggregatedLiquidationValue).HasPrecision(18, 2);
            entity.Property(e => e.CashChange).HasPrecision(18, 2);
            entity.Property(e => e.LiquidationValueChange).HasPrecision(18, 2);
            entity.Property(e => e.TotalValueChange).HasPrecision(18, 2);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("account_history");
        });

        // Configure date partitioning for better performance
        ConfigurePartitioning(modelBuilder);
    }

    private void ConfigurePartitioning(ModelBuilder modelBuilder)
    {
        // PostgreSQL partitioning will be handled through migrations
        // This is where we would set up monthly partitioning for high-volume tables
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // Default connection string - should be overridden by dependency injection
            optionsBuilder.UseNpgsql("Host=localhost;Database=vbtrader;Username=postgres;Password=your_password");
        }

        // Enable sensitive data logging in development
#if DEBUG
        optionsBuilder.EnableSensitiveDataLogging();
        optionsBuilder.LogTo(Console.WriteLine);
#endif
    }
}