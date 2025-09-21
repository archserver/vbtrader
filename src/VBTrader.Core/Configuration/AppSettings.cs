using System;

namespace VBTrader.Core.Configuration;

public class AppSettings
{
    public bool DebugMode { get; set; } = false;
    public string LogDirectory { get; set; } = "log";
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;
    public bool EnableFileLogging { get; set; } = true;
    public bool EnableConsoleLogging { get; set; } = true;
    public int MaxLogFileSizeMB { get; set; } = 100;
    public int LogRetentionDays { get; set; } = 30;

    // API Settings
    public ApiSettings Api { get; set; } = new();

    // Database Settings
    public DatabaseSettings Database { get; set; } = new();

    // Trading Settings
    public TradingSettings Trading { get; set; } = new();
}

public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5
}

public class ApiSettings
{
    public int MaxRequestsPerMinute { get; set; } = 120;
    public int MaxConcurrentRequests { get; set; } = 10;
    public int RequestTimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 1000;
    public bool EnableRequestLogging { get; set; } = false;
}

public class DatabaseSettings
{
    // Connection string will be loaded from environment variable or secure store
    public string ConnectionStringName { get; set; } = "DefaultConnection";
    public bool EnableQueryLogging { get; set; } = false;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public int MaxRetryCount { get; set; } = 3;
}

public class TradingSettings
{
    public bool RequireOrderConfirmation { get; set; } = true;
    public decimal MaxOrderValue { get; set; } = 10000;
    public int MaxDailyTrades { get; set; } = 100;
    public bool EnablePaperTrading { get; set; } = true;
}

public static class SecureConfiguration
{
    /// <summary>
    /// Gets database connection string from secure storage
    /// Priority: Environment Variable > User Secrets > Default
    /// </summary>
    public static string GetConnectionString(string name = "DefaultConnection")
    {
        // First try environment variable
        var envVar = $"CONNECTIONSTRINGS__{name.ToUpper()}";
        var connectionString = Environment.GetEnvironmentVariable(envVar);

        if (!string.IsNullOrEmpty(connectionString))
            return connectionString;

        // For development, use the local connection
        // In production, this should come from Azure Key Vault or similar
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
        {
            // Return a safe default for development
            return "Host=localhost;Database=vbtrader;Username=postgres;Password=CHANGE_ME;Port=5432";
        }

        throw new InvalidOperationException(
            $"Connection string '{name}' not found. Set the '{envVar}' environment variable.");
    }

    /// <summary>
    /// Gets API credentials from secure storage
    /// </summary>
    public static (string appKey, string appSecret) GetSchwabCredentials()
    {
        var appKey = Environment.GetEnvironmentVariable("SCHWAB_APP_KEY");
        var appSecret = Environment.GetEnvironmentVariable("SCHWAB_APP_SECRET");

        if (string.IsNullOrEmpty(appKey) || string.IsNullOrEmpty(appSecret))
        {
            throw new InvalidOperationException(
                "Schwab API credentials not found. Set SCHWAB_APP_KEY and SCHWAB_APP_SECRET environment variables.");
        }

        return (appKey, appSecret);
    }
}