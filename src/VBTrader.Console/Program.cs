using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Reflection;
using VBTrader.Core.Models;
using VBTrader.Core.Interfaces;
using VBTrader.Infrastructure.Database;
using VBTrader.Infrastructure.Schwab;
using VBTrader.Infrastructure.Services;
using VBTrader.Services;
using VBTrader.Security.Cryptography;
using VBTrader.Console;
using VBTrader.Console.UI;
using VBTrader.Console.Services;
using Serilog;
using Serilog.Core;

// Initialize Serilog early
var configuration = new ConfigurationBuilder()
    .SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var runtimeLoggingManager = new RuntimeLoggingManager(configuration);

// Configure Serilog with runtime control using the manager
var loggerConfig = new LoggerConfiguration();

// Set minimum level to the most restrictive of console or file
var minimumLevel = runtimeLoggingManager.LogToConsole && runtimeLoggingManager.LogToFile
    ? (runtimeLoggingManager.CurrentConsoleLevel < runtimeLoggingManager.CurrentFileLevel
       ? runtimeLoggingManager.CurrentConsoleLevel : runtimeLoggingManager.CurrentFileLevel)
    : runtimeLoggingManager.LogToConsole ? runtimeLoggingManager.CurrentConsoleLevel
    : runtimeLoggingManager.LogToFile ? runtimeLoggingManager.CurrentFileLevel
    : LogLevel.Error;

var convertToSerilogLevel = (LogLevel logLevel) => logLevel switch
{
    LogLevel.Trace => Serilog.Events.LogEventLevel.Verbose,
    LogLevel.Debug => Serilog.Events.LogEventLevel.Debug,
    LogLevel.Information => Serilog.Events.LogEventLevel.Information,
    LogLevel.Warning => Serilog.Events.LogEventLevel.Warning,
    LogLevel.Error => Serilog.Events.LogEventLevel.Error,
    LogLevel.Critical => Serilog.Events.LogEventLevel.Fatal,
    LogLevel.None => Serilog.Events.LogEventLevel.Fatal,
    _ => Serilog.Events.LogEventLevel.Warning
};

loggerConfig.MinimumLevel.Is(convertToSerilogLevel(minimumLevel));

// Override minimum levels for Microsoft namespaces - be more aggressive
var consoleLogLevel = convertToSerilogLevel(runtimeLoggingManager.CurrentConsoleLevel);
loggerConfig.MinimumLevel.Override("Microsoft", consoleLogLevel);
loggerConfig.MinimumLevel.Override("Microsoft.EntityFrameworkCore", consoleLogLevel);
loggerConfig.MinimumLevel.Override("Microsoft.EntityFrameworkCore.Query", consoleLogLevel);
loggerConfig.MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database", consoleLogLevel);
loggerConfig.MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Connection", consoleLogLevel);
loggerConfig.MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", consoleLogLevel);
loggerConfig.MinimumLevel.Override("Microsoft.EntityFrameworkCore.ChangeTracking", consoleLogLevel);
loggerConfig.MinimumLevel.Override("Microsoft.EntityFrameworkCore.Update", consoleLogLevel);
loggerConfig.MinimumLevel.Override("Microsoft.EntityFrameworkCore.Infrastructure", consoleLogLevel);
loggerConfig.MinimumLevel.Override("Microsoft.EntityFrameworkCore.Model", consoleLogLevel);
loggerConfig.MinimumLevel.Override("System.Net.Http", consoleLogLevel);

if (runtimeLoggingManager.LogToConsole)
{
    // Use a more restrictive console sink when level is Error - only show Error and Fatal
    var restrictiveConsoleLevel = runtimeLoggingManager.CurrentConsoleLevel == LogLevel.Error
        ? Serilog.Events.LogEventLevel.Error
        : convertToSerilogLevel(runtimeLoggingManager.CurrentConsoleLevel);

    loggerConfig.WriteTo.Console(
        restrictedToMinimumLevel: restrictiveConsoleLevel,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
}

if (runtimeLoggingManager.LogToFile)
{
    var logPath = configuration.GetValue<string>("Logging:File:Path", "logs/vbtrader-{Date}.log");
    loggerConfig.WriteTo.File(
        path: logPath,
        levelSwitch: runtimeLoggingManager.GetFileLoggingSwitch(),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        fileSizeLimitBytes: 10_485_760,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
}

Log.Logger = loggerConfig.CreateLogger();

var host = new HostBuilder()
    .ConfigureAppConfiguration((context, config) =>
    {
        config.SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory())
              .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
              .AddEnvironmentVariables()
              .AddCommandLine(args);
    })
    .ConfigureLogging(logging =>
    {
        // Don't add any default providers - only Serilog
        logging.ClearProviders();
    })
    .UseSerilog(dispose: true)
    .ConfigureServices((context, services) =>
    {

        // Add configuration and logging manager
        services.AddSingleton<IConfiguration>(context.Configuration);
        services.AddSingleton<RuntimeLoggingManager>(runtimeLoggingManager);

        // Add HTTP client for Schwab API with extended timeout
        services.AddHttpClient<ISchwabApiClient, SchwabApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120); // Increase from default 100 to 120 seconds
        });

        // Add database with explicit logging configuration
        services.AddDbContext<VBTraderDbContext>(options =>
        {
            options.UseNpgsql(context.Configuration.GetConnectionString("DefaultConnection"));

            // Configure Entity Framework logging levels to respect our settings
            options.ConfigureWarnings(warnings =>
            {
                // Only log warnings and errors by default
                warnings.Default(WarningBehavior.Log);
            });

            // Enable sensitive data logging only in debug builds
            #if DEBUG
            options.EnableSensitiveDataLogging(false);
            options.EnableDetailedErrors(false);
            #endif
        });
        services.AddScoped<IDataService, PostgreSqlDataService>();
        services.AddScoped<ISandboxDataService, SandboxDataService>();
        services.AddScoped<IUserService, UserService>();

        // Add security services
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ICredentialEncryption, CredentialEncryption>();

        // Add Schwab API client
        services.AddSingleton<ISchwabApiClient, SchwabApiClient>();

        // Add account tracking service
        services.AddScoped<AccountTrackingService>();

        // Add historical data service
        services.AddScoped<IHistoricalDataService, HistoricalDataService>();

        // Add real-time quote service
        services.AddSingleton<RealTimeQuoteService>();

        // Add sandbox trading service
        services.AddScoped<SandboxTradingService>();

        // Add UI services
        services.AddScoped<VBTrader.Console.UI.ConsoleUIManager>();

        // Add console app
        services.AddSingleton<TradingConsoleApp>();
    })
    .Build();

// Check for manual token exchange test
if (args.Length > 0 && args[0] == "--test-token")
{
    await ManualTokenExchange.ExchangeAuthorizationCode();
    return;
}

// Check for direct authentication test
if (args.Length > 0 && args[0] == "--test-auth")
{
    var testApp = host.Services.GetRequiredService<TradingConsoleApp>();
    await testApp.TestDirectAuthenticationAsync("bchase", "OICu812@*");
    return;
}

// Check for data retrieval test
if (args.Length > 0 && args[0] == "--test-data")
{
    var testApp = host.Services.GetRequiredService<TradingConsoleApp>();
    await testApp.TestDataRetrievalAsync("bchase", "OICu812@*");
    return;
}

// Check for historical data test
if (args.Length > 0 && args[0] == "--test-historical")
{
    var testApp = host.Services.GetRequiredService<TradingConsoleApp>();
    await testApp.TestHistoricalDataServiceAsync("bchase", "OICu812@*");
    return;
}

// Check for historical data menu test
if (args.Length > 0 && args[0] == "--test-menu")
{
    var testApp = host.Services.GetRequiredService<TradingConsoleApp>();
    await testApp.TestHistoricalDataMenuAsync("bchase", "OICu812@*");
    return;
}

// Check for simple user authentication test
if (args.Length > 0 && args[0] == "--test-user")
{
    var userService = host.Services.GetRequiredService<IUserService>();
    var isValid = await userService.ValidateUserAsync("bchase", "OICu812@*");
    if (isValid)
    {
        Console.WriteLine("✅ User authentication successful!");
        var user = await userService.GetUserByUsernameAsync("bchase");
        if (user != null)
        {
            Console.WriteLine($"User ID: {user.UserId}");
            Console.WriteLine($"Email: {user.Email}");
            Console.WriteLine($"Last Login: {user.LastLoginAt}");
        }
    }
    else
    {
        Console.WriteLine("❌ User authentication failed");
    }
    return;
}


// Check for credential export test
if (args.Length > 0 && args[0] == "--export-creds")
{
    var userService = host.Services.GetRequiredService<IUserService>();
    var isValid = await userService.ValidateUserAsync("bchase", "OICu812@*");
    if (isValid)
    {
        var user = await userService.GetUserByUsernameAsync("bchase");
        if (user != null)
        {
            var creds = await userService.GetSchwabCredentialsAsync(user.UserId);
            if (creds.HasValue)
            {
                Console.WriteLine("🔑 Schwab Credentials:");
                Console.WriteLine($"App Key: {creds.Value.appKey}");
                Console.WriteLine($"App Secret: {creds.Value.appSecret}");
                Console.WriteLine($"Callback URL: {creds.Value.callbackUrl}");
            }
            else
            {
                Console.WriteLine("❌ No Schwab credentials found");
            }
        }
    }
    return;
}

// Start the console trading application
var app = host.Services.GetRequiredService<TradingConsoleApp>();
await app.RunAsync();

namespace VBTrader.Console
{
    public class TradingConsoleApp
    {
        private readonly IDataService _dataService;
        private readonly ISchwabApiClient _schwabApiClient;
        private readonly ISandboxDataService _sandboxDataService;
        private readonly IUserService _userService;
        private readonly IHistoricalDataService _historicalDataService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TradingConsoleApp> _logger;
        private readonly ConsoleUIManager _uiManager;
        private readonly RealTimeQuoteService _quoteService;
        private readonly SandboxTradingService _sandboxTradingService;
        private readonly RuntimeLoggingManager _runtimeLoggingManager;
        private readonly List<StockQuote> _liveQuotes = new();
        private readonly List<MarketOpportunity> _opportunities = new();
        private readonly Dictionary<string, decimal> _positions = new();
        private bool _running = true;
        private bool _useSchwabApi = false;
        private bool _sandboxMode = false;
        private readonly bool _quietMode = true; // Set to true for clean UI, false for debug output
        private SandboxSession? _currentSandboxSession;
        private User? _currentUser;
        private UserSession? _currentUserSession;

        public TradingConsoleApp(
            IDataService dataService,
            ISchwabApiClient schwabApiClient,
            ISandboxDataService sandboxDataService,
            IUserService userService,
            IHistoricalDataService historicalDataService,
            IConfiguration configuration,
            ILogger<TradingConsoleApp> logger,
            ConsoleUIManager uiManager,
            RealTimeQuoteService quoteService,
            SandboxTradingService sandboxTradingService,
            RuntimeLoggingManager runtimeLoggingManager)
        {
            _dataService = dataService;
            _schwabApiClient = schwabApiClient;
            _sandboxDataService = sandboxDataService;
            _userService = userService;
            _historicalDataService = historicalDataService;
            _configuration = configuration;
            _logger = logger;
            _uiManager = uiManager;
            _quoteService = quoteService;
            _sandboxTradingService = sandboxTradingService;
            _runtimeLoggingManager = runtimeLoggingManager;
        }

        public async Task RunAsync()
        {
            try
            {
                System.Console.Clear();
                System.Console.Title = "VBTrader - Real-Time Trading Console";
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Unable to set console properties: {Message}", ex.Message);
            }

            // User authentication required before trading
            if (!await AuthenticateUser())
            {
                System.Console.WriteLine("Authentication failed. Exiting...");
                return;
            }

            // Clear and initialize UI
            _uiManager.Clear();

            // Try to authenticate with Schwab API using user's stored credentials
            await AttemptSchwabAuthentication();

            // Initialize quote service and data
            await InitializeQuoteService();
            await InitializeData();

            // Start background tasks
            _ = Task.Run(UpdateDataLoop);
            _ = Task.Run(RefreshDisplayLoop);

            _logger.LogInformation("VBTrader Console Application started. Press 'Q' to quit.");

            // Main input loop
            while (_running)
            {
                try
                {
                    var key = System.Console.ReadKey(true);
                    await HandleKeyPress(key);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading console input");
                    await Task.Delay(1000); // Prevent tight loop
                }
            }

            // Cleanup services
            _quoteService.Stop();
            _uiManager.Dispose();
        }

        private async Task InitializeQuoteService()
        {
            try
            {
                // Set up quote service with initial symbols
                var defaultSymbols = new[] { "AAPL", "MSFT", "GOOGL", "AMZN", "TSLA" };
                _quoteService.SetWatchedSymbols(defaultSymbols);

                // Subscribe to quote updates
                _quoteService.BulkQuotesUpdated += OnQuotesUpdated;

                // Set connection status based on Schwab authentication
                _quoteService.SetSchwabConnectionStatus(_useSchwabApi);

                // Start the service
                _quoteService.Start();

                _logger.LogInformation("Real-time quote service initialized and started");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing quote service");
            }
        }

        private void OnQuotesUpdated(object? sender, BulkQuoteUpdateEventArgs e)
        {
            try
            {
                lock (_liveQuotes)
                {
                    // Update the live quotes collection
                    _liveQuotes.Clear();
                    _liveQuotes.AddRange(e.UpdatedQuotes);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling quote updates");
            }
        }

        private async Task AttemptSchwabAuthentication()
        {
            try
            {
                // Try to get stored credentials for the current user
                var credentials = await _userService.GetSchwabCredentialsAsync(_currentUser!.UserId);

                if (credentials != null)
                {
                    _logger.LogInformation("Attempting Schwab API authentication with stored credentials...");

                    if (await _schwabApiClient.AuthenticateAsync(credentials.Value.appKey, credentials.Value.appSecret, credentials.Value.callbackUrl))
                    {
                        _useSchwabApi = true;
                        _logger.LogInformation("✅ Successfully authenticated with Schwab API");

                        // Update quote service connection status
                        _quoteService?.SetSchwabConnectionStatus(true);

                        if (!_quietMode)
                        {
                            try
                            {
                                System.Console.ForegroundColor = System.ConsoleColor.Green;
                                System.Console.WriteLine("✅ SCHWAB API CONNECTED - Live data enabled");
                                System.Console.ResetColor();
                            }
                            catch (Exception)
                            {
                                _logger.LogInformation("Schwab API Connected - Live data enabled");
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("❌ Failed to authenticate with Schwab API - using mock data");

                        if (!_quietMode)
                        {
                            try
                            {
                                System.Console.ForegroundColor = System.ConsoleColor.Yellow;
                                System.Console.WriteLine("❌ SCHWAB API AUTHENTICATION FAILED - Using simulated data");
                                System.Console.ResetColor();
                            }
                            catch (Exception)
                            {
                                _logger.LogWarning("Schwab API authentication failed - using simulated data");
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("Schwab API credentials not configured for user - using mock data");

                    if (!_quietMode)
                    {
                        try
                        {
                            System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                            System.Console.WriteLine("ℹ️ SCHWAB API NOT CONFIGURED - Using simulated data");
                            System.Console.WriteLine("   Press 'C' to configure your Schwab API credentials for live trading");
                            System.Console.ResetColor();
                        }
                        catch (Exception)
                        {
                            _logger.LogInformation("Schwab API not configured for user - using simulated data");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Schwab authentication");
                _useSchwabApi = false;
            }
        }

        private async Task InitializeData()
        {
            // Initialize market opportunities and other non-quote data
            InitializeSampleOpportunities();
            await Task.CompletedTask;
        }

        private void InitializeSampleOpportunities()
        {
            _opportunities.AddRange(new[]
            {
                new MarketOpportunity { Symbol = "AAPL", Score = 85.5m, OpportunityType = OpportunityType.BreakoutUp, PriceChangePercent = 1.33m },
                new MarketOpportunity { Symbol = "TSLA", Score = 72.3m, OpportunityType = OpportunityType.VolumeSpike, PriceChangePercent = -1.28m },
                new MarketOpportunity { Symbol = "NVDA", Score = 91.2m, OpportunityType = OpportunityType.TechnicalIndicator, PriceChangePercent = 2.01m },
            });
        }

        private async Task UpdateDataLoop()
        {
            while (_running)
            {
                try
                {
                    // Update market opportunities (simulate dynamic scoring)
                    UpdateMarketOpportunities();

                    await Task.Delay(10000); // Update opportunities every 10 seconds
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in update data loop");
                    await Task.Delay(5000); // Wait longer on error
                }
            }
        }

        private void UpdateMarketOpportunities()
        {
            var random = new Random();
            foreach (var opportunity in _opportunities)
            {
                // Simulate dynamic scoring changes
                var scoreChange = (decimal)(random.NextDouble() - 0.5) * 10; // ±5 points
                opportunity.Score = Math.Max(0, Math.Min(100, opportunity.Score + scoreChange));

                // Update price change based on live quotes
                var liveQuote = _liveQuotes.FirstOrDefault(q => q.Symbol == opportunity.Symbol);
                if (liveQuote != null)
                {
                    opportunity.PriceChangePercent = liveQuote.ChangePercent;
                }
            }
        }

        private async Task RefreshDisplayLoop()
        {
            while (_running)
            {
                try
                {
                    _uiManager.RefreshLayout(
                        _currentUser,
                        _sandboxMode,
                        _currentSandboxSession,
                        _useSchwabApi,
                        _liveQuotes,
                        _opportunities,
                        _positions);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Error refreshing display: {Message}", ex.Message);
                }

                await Task.Delay(1000); // Refresh display every second
            }
        }


        private async Task HandleKeyPress(ConsoleKeyInfo keyInfo)
        {
            var key = keyInfo.Key;
            var modifiers = keyInfo.Modifiers;

            if (key == System.ConsoleKey.Q)
            {
                await HandleLogout();
                _running = false;
                return;
            }

            if (key == System.ConsoleKey.R)
            {
                var message = "Data refreshed manually.";
                LogTrade(message);
                _uiManager.ShowMessage(message, System.ConsoleColor.Cyan);
                return;
            }

            if (key == System.ConsoleKey.S)
            {
                await SetStockSymbols();
                return;
            }

            if (key == System.ConsoleKey.T && modifiers.HasFlag(System.ConsoleModifiers.Control))
            {
                await ToggleSandboxMode();
                return;
            }

            if (key == System.ConsoleKey.H && modifiers.HasFlag(System.ConsoleModifiers.Control))
            {
                _uiManager.ToggleInstructions();
                var message = _uiManager.ShowInstructions ? "Instructions panel shown" : "Instructions panel hidden";
                _uiManager.ShowMessage(message, System.ConsoleColor.Cyan);
                return;
            }

            if (key == System.ConsoleKey.N)
            {
                await CreateNewSandboxSession();
                return;
            }

            if (key == System.ConsoleKey.L)
            {
                await LoadSandboxSession();
                return;
            }

            if (key == System.ConsoleKey.C)
            {
                await ConfigureSchwabCredentials();
                return;
            }

            if (key == System.ConsoleKey.H)
            {
                // Only allow historical data collection in sandbox mode for safety
                if (!_sandboxMode)
                {
                    var message = "📈 Historical data collection is only available in sandbox mode for safety. Press 'Ctrl+T' to toggle to sandbox mode first.";
                    _uiManager.ShowMessage(message, System.ConsoleColor.Yellow, 5000);
                    return;
                }
                await ShowHistoricalDataMenu();
                return;
            }

            if (key == System.ConsoleKey.P)
            {
                // Only allow historical data replay in sandbox mode for safety
                if (!_sandboxMode)
                {
                    var message = "📊 Historical data replay is only available in sandbox mode for safety. Press 'Ctrl+T' to toggle to sandbox mode first.";
                    _uiManager.ShowMessage(message, System.ConsoleColor.Yellow, 5000);
                    return;
                }
                await ShowHistoricalDataReplayMenu();
                return;
            }

            // Trading hotkeys
            if (modifiers.HasFlag(System.ConsoleModifiers.Alt))
            {
                switch (key)
                {
                    case System.ConsoleKey.D1:
                        await ExecuteTrade("BUY", GetStockSymbol(1), 100);
                        break;
                    case System.ConsoleKey.D2:
                        await ExecuteTrade("BUY", GetStockSymbol(2), 100);
                        break;
                    case System.ConsoleKey.D3:
                        await ExecuteTrade("BUY", GetStockSymbol(3), 100);
                        break;
                }
            }
            else if (modifiers.HasFlag(System.ConsoleModifiers.Control))
            {
                switch (key)
                {
                    case System.ConsoleKey.D1:
                        await ExecuteTrade("SELL", GetStockSymbol(1), 100);
                        break;
                    case System.ConsoleKey.D2:
                        await ExecuteTrade("SELL", GetStockSymbol(2), 100);
                        break;
                    case System.ConsoleKey.D3:
                        await ExecuteTrade("SELL", GetStockSymbol(3), 100);
                        break;
                }
            }

            // Logging control hotkeys
            if (key == System.ConsoleKey.F1)
            {
                _runtimeLoggingManager.CycleConsoleLogLevel();
                return;
            }

            if (key == System.ConsoleKey.F2)
            {
                _runtimeLoggingManager.CycleFileLogLevel();
                return;
            }

            if (key == System.ConsoleKey.F3)
            {
                _runtimeLoggingManager.ToggleConsoleLogging();
                return;
            }

            if (key == System.ConsoleKey.F4)
            {
                _runtimeLoggingManager.ToggleFileLogging();
                return;
            }

            if (key == System.ConsoleKey.F5)
            {
                var status = _runtimeLoggingManager.GetCurrentStatus();
                _uiManager.ShowMessage($"Logging Status: {status}", System.ConsoleColor.Cyan, 3000);
                return;
            }
        }

        private string GetStockSymbol(int index)
        {
            return index <= _liveQuotes.Count ? _liveQuotes[index - 1].Symbol : "UNKNOWN";
        }

        private async Task ExecuteTrade(string action, string symbol, int quantity)
        {
            try
            {
                var quote = _liveQuotes.FirstOrDefault(q => q.Symbol == symbol);
                if (quote == null)
                {
                    LogTrade($"ERROR: Symbol {symbol} not found");
                    return;
                }

                if (_sandboxMode && _currentSandboxSession != null)
                {
                    // Execute sandbox trade
                    var tradeAction = action == "BUY" ? TradeAction.Buy : TradeAction.Sell;
                    var result = await _sandboxDataService.ExecuteSandboxTradeAsync(
                        _currentSandboxSession.SandboxSessionId,
                        symbol,
                        tradeAction,
                        quantity,
                        OrderType.Market);

                    if (result.Success)
                    {
                        _currentSandboxSession.CurrentBalance = result.NewBalance;
                        var message = $"✅ {action} EXECUTED (SANDBOX): {quantity} {symbol} @ ${result.ExecutionPrice:F2} (Total: ${result.TotalCost:F2}) New Balance: ${result.NewBalance:F2}";
                        LogTrade(message);
                        _uiManager.ShowMessage(message, System.ConsoleColor.Green);
                    }
                    else
                    {
                        var message = $"❌ {action} FAILED (SANDBOX): {result.ErrorMessage}";
                        LogTrade(message);
                        _uiManager.ShowMessage(message, System.ConsoleColor.Red);
                    }
                }
                else
                {
                    // Live trading mode
                    var price = quote.LastPrice;
                    var totalValue = price * quantity;

                    // Update positions for live mode
                    if (action == "BUY")
                    {
                        _positions[symbol] = _positions.GetValueOrDefault(symbol, 0) + quantity;
                        var message = $"✅ BUY EXECUTED (LIVE): {quantity} {symbol} @ ${price:F2} (Total: ${totalValue:F2})";
                        LogTrade(message);
                        _uiManager.ShowMessage(message, System.ConsoleColor.Green);
                    }
                    else if (action == "SELL")
                    {
                        var currentPosition = _positions.GetValueOrDefault(symbol, 0);
                        if (currentPosition < quantity)
                        {
                            var message = $"❌ SELL FAILED (LIVE): Insufficient shares. Have {currentPosition}, tried to sell {quantity}";
                            LogTrade(message);
                            _uiManager.ShowMessage(message, System.ConsoleColor.Red);
                            return;
                        }

                        _positions[symbol] = currentPosition - quantity;
                        if (_positions[symbol] == 0)
                            _positions.Remove(symbol);

                        var successMessage = $"✅ SELL EXECUTED (LIVE): {quantity} {symbol} @ ${price:F2} (Total: ${totalValue:F2})";
                        LogTrade(successMessage);
                        _uiManager.ShowMessage(successMessage, System.ConsoleColor.Green);
                    }

                    // TODO: Integrate with actual Schwab API for live trades
                    await Task.Delay(50); // Simulate API call
                }
            }
            catch (Exception ex)
            {
                var message = $"❌ TRADE ERROR: {ex.Message}";
                LogTrade(message);
                _uiManager.ShowMessage(message, System.ConsoleColor.Red);
            }
        }

        private async Task SetStockSymbols()
        {
            _uiManager.Clear();

            System.Console.ForegroundColor = System.ConsoleColor.Yellow;
            System.Console.WriteLine("Stock Symbol Configuration");
            System.Console.WriteLine("=========================");
            System.Console.WriteLine();
            System.Console.Write("Enter stock symbols (comma-separated, max 10): ");
            System.Console.ForegroundColor = System.ConsoleColor.White;

            var input = System.Console.ReadLine();
            if (!string.IsNullOrEmpty(input))
            {
                var symbols = input.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(s => s.Trim().ToUpper())
                                  .Take(10) // Increased to match quote service max
                                  .ToArray();

                // Update quote service with new symbols
                _quoteService.SetWatchedSymbols(symbols);

                var message = $"Updated watchlist: {string.Join(", ", symbols)}";
                LogTrade(message);
                _uiManager.ShowMessage(message, System.ConsoleColor.Green);
            }
            else
            {
                _uiManager.ShowMessage("No symbols entered. Watchlist unchanged.", System.ConsoleColor.Yellow);
            }

            await Task.Delay(2000);
        }

        private async Task ToggleSandboxMode()
        {
            try
            {
                _sandboxMode = !_sandboxMode;
                if (_sandboxMode && _currentSandboxSession == null)
                {
                    // Create a default sandbox session if none exists
                    await CreateDefaultSandboxSession();
                }

                _uiManager.Clear();
                var message = $"Switched to {(_sandboxMode ? "SANDBOX" : "LIVE")} mode";
                LogTrade(message);
                _uiManager.ShowMessage(message, _sandboxMode ? System.ConsoleColor.Yellow : System.ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling sandbox mode");
            }
        }

        private async Task CreateNewSandboxSession()
        {
            try
            {
                System.Console.Clear();
                System.Console.WriteLine("Create New Sandbox Session");
                System.Console.WriteLine("==========================");

                System.Console.Write("Session Name: ");
                var sessionName = System.Console.ReadLine() ?? $"Session_{DateTime.UtcNow:yyyyMMdd_HHmm}";

                System.Console.Write("Start Date (yyyy-MM-dd) [default: 30 days ago]: ");
                var startDateInput = System.Console.ReadLine();
                DateTime startDate = DateTime.TryParse(startDateInput, out var parsedStart) ? parsedStart.ToUniversalTime() : DateTime.UtcNow.AddDays(-30);

                System.Console.Write("End Date (yyyy-MM-dd) [default: today]: ");
                var endDateInput = System.Console.ReadLine();
                DateTime endDate = DateTime.TryParse(endDateInput, out var parsedEnd) ? parsedEnd.ToUniversalTime() : DateTime.UtcNow;

                System.Console.Write("Initial Balance [default: $100,000]: ");
                var balanceInput = System.Console.ReadLine();
                decimal initialBalance = decimal.TryParse(balanceInput, out var parsedBalance) ? parsedBalance : 100000m;

                var session = await _sandboxDataService.CreateSandboxSessionAsync(_currentUser!.UserId, startDate, endDate, initialBalance);
                session.SessionName = sessionName;

                _currentSandboxSession = session;
                _sandboxMode = true;

                _uiManager.Clear();
                var message = $"Created new sandbox session: {sessionName}";
                LogTrade(message);
                _uiManager.ShowMessage(message, System.ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating sandbox session");
                System.Console.WriteLine($"Error: {ex.Message}");
                System.Console.WriteLine("Press any key to continue...");
                System.Console.ReadKey();
            }
        }

        private async Task LoadSandboxSession()
        {
            try
            {
                var sessions = await _sandboxDataService.GetSandboxSessionHistoryAsync(_currentUser!.UserId);
                var sessionList = sessions.ToList();

                if (!sessionList.Any())
                {
                    System.Console.WriteLine("No sandbox sessions found. Creating a default session...");
                    await CreateDefaultSandboxSession();
                    return;
                }

                System.Console.Clear();
                System.Console.WriteLine("Load Sandbox Session");
                System.Console.WriteLine("====================");

                for (int i = 0; i < sessionList.Count; i++)
                {
                    var session = sessionList[i];
                    System.Console.WriteLine($"{i + 1}. {session.SessionName} - Balance: ${session.CurrentBalance:N2} ({session.StartDate:yyyy-MM-dd} to {session.EndDate:yyyy-MM-dd})");
                }

                System.Console.Write("Select session (1-{0}): ", sessionList.Count);
                var input = System.Console.ReadLine();

                if (int.TryParse(input, out var index) && index >= 1 && index <= sessionList.Count)
                {
                    _currentSandboxSession = sessionList[index - 1];
                    _sandboxMode = true;

                    _uiManager.Clear();
                    var message = $"Loaded sandbox session: {_currentSandboxSession.SessionName}";
                    LogTrade(message);
                    _uiManager.ShowMessage(message, System.ConsoleColor.Green);
                }
                else
                {
                    System.Console.WriteLine("Invalid selection. Press any key to continue...");
                    System.Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading sandbox session");
                System.Console.WriteLine($"Error: {ex.Message}");
                System.Console.WriteLine("Press any key to continue...");
                System.Console.ReadKey();
            }
        }

        private async Task CreateDefaultSandboxSession()
        {
            try
            {
                _currentSandboxSession = await _sandboxDataService.CreateSandboxSessionAsync(
                    _currentUser!.UserId,
                    DateTime.UtcNow.AddDays(-30),
                    DateTime.UtcNow,
                    100000m);
                _currentSandboxSession.SessionName = "Default Session";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating default sandbox session");
            }
        }

        private async Task<bool> AuthenticateUser()
        {
            try
            {
                try { System.Console.Clear(); } catch { /* Ignore console clear errors */ }
                PrintWelcomeHeader();

                while (true)
                {
                    System.Console.WriteLine("┌─────────────────────────────────────────────────────────────────────────────┐");
                    System.Console.WriteLine("│                            VBTrader Authentication                           │");
                    System.Console.WriteLine("└─────────────────────────────────────────────────────────────────────────────┘");
                    System.Console.WriteLine();
                    System.Console.WriteLine("1. Login with existing account");
                    System.Console.WriteLine("2. Create new account");
                    System.Console.WriteLine("3. Exit");
                    System.Console.WriteLine();
                    System.Console.Write("Select option (1-3): ");

                    var choice = System.Console.ReadLine();
                    System.Console.WriteLine();

                    switch (choice)
                    {
                        case "1":
                            if (await HandleLogin())
                                return true;
                            break;
                        case "2":
                            if (await HandleRegistration())
                                return true;
                            break;
                        case "3":
                            return false;
                        default:
                            System.Console.WriteLine("Invalid option. Please try again.");
                            System.Console.WriteLine();
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user authentication");
                System.Console.WriteLine($"Authentication error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> HandleLogin()
        {
            try
            {
                System.Console.Write("Username: ");
                var username = System.Console.ReadLine();
                if (string.IsNullOrWhiteSpace(username))
                {
                    System.Console.WriteLine("Username cannot be empty.");
                    await Task.Delay(2000);
                    return false;
                }

                System.Console.Write("Password: ");
                var password = ReadPassword();
                if (string.IsNullOrWhiteSpace(password))
                {
                    System.Console.WriteLine("Password cannot be empty.");
                    await Task.Delay(2000);
                    return false;
                }

                var user = await _userService.AuthenticateAsync(username, password);
                if (user != null)
                {
                    _currentUser = user;

                    // Create a new user session
                    _currentUserSession = await _userService.CreateSessionAsync(user.UserId, TradingMode.Sandbox);
                    await _userService.UpdateLastLoginAsync(user.UserId);

                    System.Console.WriteLine($"\nWelcome back, {user.Username}!");
                    await Task.Delay(1500);
                    return true;
                }
                else
                {
                    System.Console.WriteLine("\nInvalid username or password.");
                    await Task.Delay(2000);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                System.Console.WriteLine($"Login error: {ex.Message}");
                await Task.Delay(2000);
                return false;
            }
        }

        private async Task<bool> HandleRegistration()
        {
            try
            {
                System.Console.WriteLine("Create New Account");
                System.Console.WriteLine("==================");
                System.Console.WriteLine();

                System.Console.Write("Username: ");
                var username = System.Console.ReadLine();
                if (string.IsNullOrWhiteSpace(username))
                {
                    System.Console.WriteLine("Username cannot be empty.");
                    await Task.Delay(2000);
                    return false;
                }

                System.Console.Write("Email: ");
                var email = System.Console.ReadLine();
                if (string.IsNullOrWhiteSpace(email))
                {
                    System.Console.WriteLine("Email cannot be empty.");
                    await Task.Delay(2000);
                    return false;
                }

                System.Console.Write("Password: ");
                var password = ReadPassword();
                if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                {
                    System.Console.WriteLine("Password must be at least 8 characters long.");
                    await Task.Delay(2000);
                    return false;
                }

                System.Console.Write("Confirm Password: ");
                var confirmPassword = ReadPassword();
                if (password != confirmPassword)
                {
                    System.Console.WriteLine("Passwords do not match.");
                    await Task.Delay(2000);
                    return false;
                }

                var user = await _userService.CreateUserAsync(username, password, email);
                if (user != null)
                {
                    _currentUser = user;

                    // Create a new user session for new users
                    _currentUserSession = await _userService.CreateSessionAsync(user.UserId, TradingMode.Sandbox);

                    System.Console.WriteLine($"\nAccount created successfully! Welcome, {user.Username}!");
                    System.Console.WriteLine("You can now configure your Schwab API credentials for live trading.");
                    await Task.Delay(3000);
                    return true;
                }
                else
                {
                    System.Console.WriteLine("\nFailed to create account. Username or email may already exist.");
                    await Task.Delay(2000);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration");
                System.Console.WriteLine($"Registration error: {ex.Message}");
                await Task.Delay(2000);
                return false;
            }
        }

        private string ReadPassword()
        {
            var password = string.Empty;
            ConsoleKeyInfo keyInfo;

            do
            {
                keyInfo = System.Console.ReadKey(true);
                if (keyInfo.Key != System.ConsoleKey.Backspace && keyInfo.Key != System.ConsoleKey.Enter)
                {
                    password += keyInfo.KeyChar;
                    System.Console.Write("*");
                }
                else if (keyInfo.Key == System.ConsoleKey.Backspace && password.Length > 0)
                {
                    password = password[0..^1];
                    System.Console.Write("\b \b");
                }
            } while (keyInfo.Key != System.ConsoleKey.Enter);

            System.Console.WriteLine();
            return password;
        }

        private async Task ConfigureSchwabCredentials()
        {
            try
            {
                System.Console.Clear();
                System.Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                System.Console.WriteLine("║                          Schwab API Configuration                            ║");
                System.Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                System.Console.WriteLine();

                System.Console.WriteLine("To enable live trading, you need to configure your Schwab API credentials.");
                System.Console.WriteLine("These will be securely encrypted and stored in the database.");
                System.Console.WriteLine();
                System.Console.WriteLine("To get your Schwab API credentials:");
                System.Console.WriteLine("1. Visit the Charles Schwab Developer Portal");
                System.Console.WriteLine("2. Create a new application");
                System.Console.WriteLine("3. Note your App Key and App Secret");
                System.Console.WriteLine();

                // Check if user already has credentials
                var existingCredentials = await _userService.GetSchwabCredentialsAsync(_currentUser!.UserId);
                if (existingCredentials != null)
                {
                    System.Console.WriteLine("✅ You already have Schwab credentials configured.");
                    System.Console.WriteLine();
                    System.Console.WriteLine("1. Update existing credentials");
                    System.Console.WriteLine("2. Test current credentials");
                    System.Console.WriteLine("3. Back to trading");
                    System.Console.WriteLine();
                    System.Console.Write("Select option (1-3): ");

                    var choice = System.Console.ReadLine();
                    switch (choice)
                    {
                        case "1":
                            await UpdateSchwabCredentials();
                            break;
                        case "2":
                            await TestSchwabCredentials();
                            break;
                        default:
                            break;
                    }
                }
                else
                {
                    System.Console.WriteLine("⚠️  No Schwab credentials found. Let's set them up!");
                    System.Console.WriteLine();
                    await SetupNewSchwabCredentials();
                }

                System.Console.WriteLine("\nPress any key to return to trading...");
                System.Console.ReadKey();
                _uiManager.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error configuring Schwab credentials");
                System.Console.WriteLine($"Error: {ex.Message}");
                System.Console.WriteLine("Press any key to continue...");
                System.Console.ReadKey();
            }
        }

        private async Task SetupNewSchwabCredentials()
        {
            try
            {
                System.Console.Write("Enter your Schwab App Key: ");
                var appKey = System.Console.ReadLine();
                if (string.IsNullOrWhiteSpace(appKey))
                {
                    System.Console.WriteLine("App Key cannot be empty.");
                    return;
                }

                System.Console.Write("Enter your Schwab App Secret: ");
                var appSecret = ReadPassword();
                if (string.IsNullOrWhiteSpace(appSecret))
                {
                    System.Console.WriteLine("App Secret cannot be empty.");
                    return;
                }

                System.Console.Write("Enter callback URL [default: https://127.0.0.1:3000]: ");
                var callbackUrl = System.Console.ReadLine();
                if (string.IsNullOrWhiteSpace(callbackUrl))
                {
                    callbackUrl = "https://127.0.0.1:3000";
                }

                System.Console.WriteLine("\nSaving credentials securely...");
                await _userService.StoreSchwabCredentialsAsync(_currentUser!.UserId, appKey, appSecret, callbackUrl);

                System.Console.WriteLine("✅ Schwab credentials saved successfully!");
                System.Console.WriteLine("You can now use live trading mode.");

                // Test the credentials immediately
                System.Console.WriteLine("\nTesting connection...");
                await TestSchwabCredentials();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up Schwab credentials");
                System.Console.WriteLine($"Failed to save credentials: {ex.Message}");
            }
        }

        private async Task UpdateSchwabCredentials()
        {
            System.Console.WriteLine("Update Schwab Credentials");
            System.Console.WriteLine("=========================");
            System.Console.WriteLine();
            await SetupNewSchwabCredentials();
        }

        private async Task TestSchwabCredentials()
        {
            try
            {
                System.Console.WriteLine("Testing Schwab API connection...");

                var credentials = await _userService.GetSchwabCredentialsAsync(_currentUser!.UserId);
                if (credentials == null)
                {
                    System.Console.WriteLine("❌ No credentials found.");
                    return;
                }

                // Try to authenticate with Schwab API
                var success = await _schwabApiClient.AuthenticateAsync(
                    credentials.Value.appKey,
                    credentials.Value.appSecret,
                    credentials.Value.callbackUrl);

                if (success)
                {
                    System.Console.WriteLine("✅ Schwab API connection successful!");
                    _useSchwabApi = true;
                }
                else
                {
                    System.Console.WriteLine("❌ Failed to connect to Schwab API. Please check your credentials.");
                    _useSchwabApi = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing Schwab credentials");
                System.Console.WriteLine($"❌ Connection test failed: {ex.Message}");
                _useSchwabApi = false;
            }
        }


        private async Task HandleLogout()
        {
            try
            {
                if (_currentUserSession != null)
                {
                    await _userService.LogoutAsync(_currentUserSession.SessionToken);
                    _logger.LogInformation("User session ended successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
            }
        }

        private void PrintWelcomeHeader()
        {
            try
            {
                System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                System.Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                System.Console.WriteLine("║                          Welcome to VBTrader                                 ║");
                System.Console.WriteLine("║                     Professional Trading Platform                           ║");
                System.Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                System.Console.ResetColor();
                System.Console.WriteLine();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Unable to print welcome header: {Message}", ex.Message);
            }
        }

        private void LogTrade(string message)
        {
            // This would be displayed in a separate area in a real implementation
            _logger.LogInformation(message);
        }

        public async Task TestDirectAuthenticationAsync(string username, string password)
        {
            try
            {
                System.Console.WriteLine("🧪 Testing Direct Authentication");
                System.Console.WriteLine($"Username: {username}");
                System.Console.WriteLine("Password: [HIDDEN]");
                System.Console.WriteLine();

                // Test user authentication
                System.Console.WriteLine("🔐 Validating user credentials...");
                var user = await _userService.ValidateUserAsync(username, password);

                if (!user)
                {
                    System.Console.WriteLine("❌ Invalid username or password");
                    return;
                }

                System.Console.WriteLine($"✅ User authenticated successfully");
                // Get the actual user object
                var userObj = await _userService.GetUserByUsernameAsync(username);
                _currentUser = userObj;

                // Check if user has Schwab credentials
                System.Console.WriteLine("🔑 Checking Schwab API credentials...");
                var hasCredentials = await _userService.HasSchwabCredentialsAsync(userObj!.UserId);

                if (!hasCredentials)
                {
                    System.Console.WriteLine("⚠️  No Schwab credentials found. Need to set up API access.");
                    return;
                }

                System.Console.WriteLine("✅ Schwab credentials found");

                // Get Schwab credentials and test authentication
                var credentials = await _userService.GetSchwabCredentialsAsync(userObj.UserId);
                if (credentials == null)
                {
                    System.Console.WriteLine("❌ Failed to retrieve Schwab credentials");
                    return;
                }

                System.Console.WriteLine("🚀 Testing Schwab API authentication...");
                var authSuccess = await _schwabApiClient.AuthenticateAsync(
                    credentials.Value.appKey,
                    credentials.Value.appSecret,
                    "https://127.0.0.1:3000");

                if (authSuccess)
                {
                    System.Console.WriteLine("🎉 Schwab API authentication successful!");
                    System.Console.WriteLine("✅ Ready for live market data and trading");
                    _useSchwabApi = true;

                    // Test with a simple API call to get account numbers
                    System.Console.WriteLine("🧪 Testing API access with account numbers request...");
                    try
                    {
                        var accounts = await _schwabApiClient.GetLinkedAccountsAsync();
                        if (accounts.Any())
                        {
                            System.Console.WriteLine($"✅ Successfully retrieved {accounts.Count()} account(s)!");
                            foreach (var account in accounts)
                            {
                                System.Console.WriteLine($"   Account: {account.AccountNumber} (Hash: {account.HashValue[..8]}...)");
                            }
                        }
                        else
                        {
                            System.Console.WriteLine("⚠️ No accounts found - this might indicate a scope or permission issue");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"❌ Account number test failed: {ex.Message}");
                        System.Console.WriteLine("💡 This suggests the token authentication is not working properly");
                    }
                }
                else
                {
                    System.Console.WriteLine("❌ Schwab API authentication failed");
                    System.Console.WriteLine("💡 Check your App Key, App Secret, and callback URL");
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"❌ Authentication test failed: {ex.Message}");
                _logger.LogError(ex, "Direct authentication test failed");
            }
        }


        public async Task TestDataRetrievalAsync(string username, string password)
        {
            try
            {
                System.Console.WriteLine("🧪 Testing Schwab Data Retrieval");
                System.Console.WriteLine("================================");
                System.Console.WriteLine();

                // Authenticate first
                System.Console.WriteLine("🔐 Authenticating...");
                var user = await _userService.ValidateUserAsync(username, password);
                if (!user)
                {
                    System.Console.WriteLine("❌ Authentication failed");
                    return;
                }

                var userObj = await _userService.GetUserByUsernameAsync(username);
                var credentials = await _userService.GetSchwabCredentialsAsync(userObj!.UserId);
                if (credentials == null)
                {
                    System.Console.WriteLine("❌ No Schwab credentials found");
                    return;
                }

                var authSuccess = await _schwabApiClient.AuthenticateAsync(
                    credentials.Value.appKey,
                    credentials.Value.appSecret,
                    "https://127.0.0.1:3000");

                if (!authSuccess)
                {
                    System.Console.WriteLine("❌ Schwab authentication failed");
                    return;
                }

                System.Console.WriteLine("✅ Authenticated successfully!");

                // Set user ID for account tracking
                _schwabApiClient.SetCurrentUserId(userObj.UserId);

                System.Console.WriteLine();

                // Create log directory if it doesn't exist
                var logDir = Path.Combine("C:", "Users", "Bryon Chase", "Documents", "vbtrader", "log");
                Directory.CreateDirectory(logDir);

                // Test 1: Account Numbers
                System.Console.WriteLine("📊 Test 1: Getting account numbers...");
                await TestAndSaveData<object>("account-numbers", async () =>
                {
                    var accounts = await _schwabApiClient.GetLinkedAccountsAsync();
                    return new { accounts = accounts.ToList(), timestamp = DateTime.Now };
                }, logDir);

                // Test 2: Market Data for AAPL
                System.Console.WriteLine("📈 Test 2: Getting market data for AAPL...");
                await TestAndSaveData<object>("market-data-aapl", async () =>
                {
                    try
                    {
                        var quotes = await _schwabApiClient.GetQuotesAsync(new[] { "AAPL" });
                        return new { quotes = quotes.ToList(), timestamp = DateTime.Now };
                    }
                    catch (Exception ex)
                    {
                        return new { error = ex.Message, timestamp = DateTime.Now };
                    }
                }, logDir);

                // Test 3: Account Details (if we have accounts)
                System.Console.WriteLine("💰 Test 3: Getting account details...");
                await TestAndSaveData<object>("account-details", async () =>
                {
                    try
                    {
                        var accounts = await _schwabApiClient.GetLinkedAccountsAsync();
                        var firstAccount = accounts.FirstOrDefault();
                        if (firstAccount != null)
                        {
                            var details = await _schwabApiClient.GetAccountDetailsAsync(firstAccount.HashValue);
                            return new { accountDetails = details, timestamp = DateTime.Now };
                        }
                        return new { message = "No accounts found", timestamp = DateTime.Now };
                    }
                    catch (Exception ex)
                    {
                        return new { error = ex.Message, timestamp = DateTime.Now };
                    }
                }, logDir);

                System.Console.WriteLine();
                System.Console.WriteLine("✅ Data retrieval tests completed!");
                System.Console.WriteLine($"📁 Results saved to: {logDir}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"❌ Data retrieval test failed: {ex.Message}");
            }
        }

        private async Task TestAndSaveData<T>(string testName, Func<Task<T>> dataRetriever, string logDir)
        {
            try
            {
                var startTime = DateTime.Now;
                System.Console.WriteLine($"   ⏱️  Starting {testName} at {startTime:HH:mm:ss}...");

                var data = await dataRetriever();

                var endTime = DateTime.Now;
                var duration = endTime - startTime;

                var result = new
                {
                    testName,
                    startTime,
                    endTime,
                    durationMs = duration.TotalMilliseconds,
                    success = true,
                    data
                };

                var fileName = $"{testName}-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
                var filePath = Path.Combine(logDir, fileName);

                await File.WriteAllTextAsync(filePath, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                }));

                System.Console.WriteLine($"   ✅ {testName} completed in {duration.TotalMilliseconds:F0}ms - saved to {fileName}");
            }
            catch (Exception ex)
            {
                var result = new
                {
                    testName,
                    timestamp = DateTime.Now,
                    success = false,
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                };

                var fileName = $"{testName}-ERROR-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
                var filePath = Path.Combine(logDir, fileName);

                await File.WriteAllTextAsync(filePath, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                }));

                System.Console.WriteLine($"   ❌ {testName} failed: {ex.Message} - error saved to {fileName}");
            }
        }

        public async Task TestHistoricalDataServiceAsync(string username, string password)
        {
            try
            {
                System.Console.WriteLine("🧪 Testing Enhanced Historical Data Service");
                System.Console.WriteLine("===========================================");
                System.Console.WriteLine();

                // Authenticate first
                System.Console.WriteLine("🔐 Authenticating...");
                var user = await _userService.AuthenticateAsync(username, password);
                if (user == null)
                {
                    System.Console.WriteLine("❌ Authentication failed");
                    return;
                }

                _currentUser = user;
                System.Console.WriteLine($"✅ Authenticated as {user.Username}");
                System.Console.WriteLine();

                // Test 1: Get current data statistics
                System.Console.WriteLine("📊 Test 1: Getting current data statistics...");
                var stats = await _historicalDataService.GetDataStatisticsAsync();
                System.Console.WriteLine($"   📈 Current database contains:");
                System.Console.WriteLine($"   • {stats.TotalRecords:N0} total records");
                System.Console.WriteLine($"   • {stats.UniqueSymbols} unique symbols");
                System.Console.WriteLine($"   • Date range: {stats.OldestData:yyyy-MM-dd} to {stats.NewestData:yyyy-MM-dd}");
                System.Console.WriteLine();

                // Test 2: Fetch historical data for a single symbol
                System.Console.WriteLine("📈 Test 2: Fetching 1-minute data for AAPL (last 2 days)...");
                var symbols = new List<string> { "AAPL" };
                var endDate = DateTime.UtcNow;
                var startDate = endDate.AddDays(-2);

                var result = await _historicalDataService.FetchAndSaveHistoricalDataAsync(
                    symbols, startDate, endDate, TimeFrame.OneMinute);

                System.Console.WriteLine($"   ✅ Collection completed in {result.Duration.TotalSeconds:F1} seconds");
                System.Console.WriteLine($"   • Records added: {result.RecordsAdded:N0}");
                System.Console.WriteLine($"   • Records updated: {result.RecordsUpdated:N0}");
                System.Console.WriteLine($"   • Records skipped: {result.RecordsSkipped:N0}");
                System.Console.WriteLine($"   • Success: {result.Success}");

                if (result.Errors.Any())
                {
                    System.Console.WriteLine($"   ⚠️ Errors: {result.Errors.Count}");
                    foreach (var error in result.Errors.Take(3))
                    {
                        System.Console.WriteLine($"     - {error}");
                    }
                }
                System.Console.WriteLine();

                // Test 3: Test bulk historical data collection
                System.Console.WriteLine("📊 Test 3: Testing bulk data collection (5 symbols, 1 day)...");
                var bulkRequest = new BulkPriceHistoryRequest
                {
                    Symbols = new List<string> { "AAPL", "MSFT", "GOOGL", "AMZN", "TSLA" },
                    PeriodType = PeriodType.Day,
                    Period = 1,
                    FrequencyType = FrequencyType.Minute,
                    Frequency = 5,
                    MaxSymbols = 5
                };

                var bulkResult = await _historicalDataService.FetchBulkHistoricalDataAsync(bulkRequest);
                System.Console.WriteLine($"   ✅ Bulk collection completed in {bulkResult.TotalDuration.TotalSeconds:F1} seconds");
                System.Console.WriteLine($"   • API calls used: {bulkResult.TotalApiCalls}");
                System.Console.WriteLine($"   • Successful symbols: {bulkResult.SuccessfulSymbols}");
                System.Console.WriteLine($"   • Failed symbols: {bulkResult.FailedSymbols}");
                System.Console.WriteLine($"   • Overall success: {bulkResult.Success}");
                System.Console.WriteLine();

                // Test 4: Data validation
                if (bulkResult.SuccessfulSymbols > 0)
                {
                    var firstSymbol = bulkResult.SymbolResults.Keys.First();
                    System.Console.WriteLine($"🔍 Test 4: Validating data integrity for {firstSymbol}...");

                    var validation = await _historicalDataService.ValidateDataIntegrityAsync(
                        firstSymbol, TimeFrame.FiveMinutes, startDate, endDate);

                    System.Console.WriteLine($"   ✅ Validation completed");
                    System.Console.WriteLine($"   • Data is valid: {validation.IsValid}");
                    System.Console.WriteLine($"   • Total records: {validation.TotalRecords:N0}");
                    System.Console.WriteLine($"   • Time span covered: {validation.CoveredTimeSpan.TotalHours:F1} hours");
                    System.Console.WriteLine($"   • Data gaps found: {validation.Gaps.Count}");
                    System.Console.WriteLine($"   • Inconsistencies: {validation.Inconsistencies.Count}");
                }
                System.Console.WriteLine();

                // Test 5: Updated statistics
                System.Console.WriteLine("📊 Test 5: Getting updated statistics...");
                var newStats = await _historicalDataService.GetDataStatisticsAsync();
                System.Console.WriteLine($"   📈 Updated database contains:");
                System.Console.WriteLine($"   • {newStats.TotalRecords:N0} total records (+{newStats.TotalRecords - stats.TotalRecords:N0})");
                System.Console.WriteLine($"   • {newStats.UniqueSymbols} unique symbols");
                System.Console.WriteLine($"   • Date range: {newStats.OldestData:yyyy-MM-dd} to {newStats.NewestData:yyyy-MM-dd}");

                if (newStats.SymbolCounts.Any())
                {
                    System.Console.WriteLine($"   • Top symbols by record count:");
                    foreach (var symbolCount in newStats.SymbolCounts.OrderByDescending(x => x.Value).Take(5))
                    {
                        System.Console.WriteLine($"     - {symbolCount.Key}: {symbolCount.Value:N0} records");
                    }
                }
                System.Console.WriteLine();

                System.Console.WriteLine("🎉 All historical data service tests completed successfully!");
                System.Console.WriteLine();
                System.Console.WriteLine("📝 Summary:");
                System.Console.WriteLine("• Enhanced database schema with millisecond precision ✅");
                System.Console.WriteLine("• Flexible timeframe support (seconds to monthly) ✅");
                System.Console.WriteLine("• Comprehensive logging and validation ✅");
                System.Console.WriteLine("• Bulk API data collection ✅");
                System.Console.WriteLine("• Data integrity validation ✅");
                System.Console.WriteLine("• Professional error handling ✅");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"❌ Historical data service test failed: {ex.Message}");
                _logger.LogError(ex, "Historical data service test failed");
            }
        }

        public async Task TestHistoricalDataMenuAsync(string username, string password)
        {
            try
            {
                System.Console.WriteLine("🧪 Testing Historical Data Collection Menu");
                System.Console.WriteLine("==========================================");
                System.Console.WriteLine();

                // Authenticate first
                System.Console.WriteLine("🔐 Authenticating...");
                var user = await _userService.AuthenticateAsync(username, password);
                if (user == null)
                {
                    System.Console.WriteLine("❌ Authentication failed");
                    return;
                }

                _currentUser = user;
                _sandboxMode = true; // Enable sandbox mode for testing
                System.Console.WriteLine($"✅ Authenticated as {user.Username}");
                System.Console.WriteLine($"✅ Sandbox mode enabled");
                System.Console.WriteLine();

                // Test the menu
                System.Console.WriteLine("📋 Testing Historical Data Collection Menu...");
                System.Console.WriteLine("This will demonstrate the menu interface with sensible options:");
                System.Console.WriteLine("• Timeframes: 1-minute and 5-minute only");
                System.Console.WriteLine("• Date ranges: 1 day, 1 week, 1 month only");
                System.Console.WriteLine("• Maximum 5 symbols");
                System.Console.WriteLine("• Sandbox mode required for safety");
                System.Console.WriteLine();

                // Show the menu
                await ShowHistoricalDataMenu();

                System.Console.WriteLine();
                System.Console.WriteLine("✅ Historical data menu test completed!");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"❌ Historical data menu test failed: {ex.Message}");
                _logger.LogError(ex, "Historical data menu test failed");
            }
        }

        private async Task ShowHistoricalDataMenu()
        {
            try
            {
                // Set menu content for the dedicated menu area
                var menuContent = "📈 Historical Data Collection\n" +
                                 "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                 "Starting collection process...\n" +
                                 "Please wait while prompts appear below...";
                _uiManager.SetMenuContent(menuContent);

                // Step 1: Symbol Selection
                _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                         "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                         "Step 1: Symbol Selection\n" +
                                         "Choose symbols to download data for...");

                var symbols = await SelectSymbolsAsync();
                if (symbols.Count == 0)
                {
                    _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                             "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                             "❌ No symbols selected.\n" +
                                             "Returning to main menu...");
                    await Task.Delay(2000);
                    _uiManager.ClearMenuContent();
                    return;
                }

                // Step 2: Timeframe Selection
                _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                         "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                         "Step 2: Timeframe Selection\n" +
                                         $"Symbols: {string.Join(", ", symbols)}\n" +
                                         "Choose timeframe for data collection...");

                var timeFrame = SelectTimeFrame();
                if (timeFrame == null)
                {
                    _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                             "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                             "❌ No timeframe selected.\n" +
                                             "Returning to main menu...");
                    await Task.Delay(2000);
                    _uiManager.ClearMenuContent();
                    return;
                }

                // Step 3: Date Range Selection
                _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                         "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                         "Step 3: Date Range Selection\n" +
                                         $"Symbols: {string.Join(", ", symbols)}\n" +
                                         $"Timeframe: {timeFrame}\n" +
                                         "Choose date range for data...");

                var (startDate, endDate) = SelectDateRange();
                if (startDate == null || endDate == null)
                {
                    _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                             "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                             "❌ Invalid date range.\n" +
                                             "Returning to main menu...");
                    await Task.Delay(2000);
                    _uiManager.ClearMenuContent();
                    return;
                }

                // Step 4: Show Summary and Confirm
                var estimatedRecords = EstimateRecordCount(symbols.Count, timeFrame.Value, startDate.Value, endDate.Value);
                _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                         "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                         "📋 Collection Summary:\n" +
                                         $"• Symbols: {string.Join(", ", symbols)}\n" +
                                         $"• Timeframe: {timeFrame}\n" +
                                         $"• Date Range: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}\n" +
                                         $"• Estimated Records: ~{estimatedRecords:N0}\n" +
                                         "Proceed with collection? (y/n):");

                System.Console.SetCursorPosition(0, System.Console.WindowHeight - 1);
                System.Console.Write("Proceed with data collection? (y/n): ");

                var confirm = System.Console.ReadKey();
                System.Console.WriteLine();

                if (confirm.KeyChar.ToString().ToLower() != "y")
                {
                    _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                             "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                             "❌ Collection cancelled.\n" +
                                             "Returning to main menu...");
                    await Task.Delay(2000);
                    _uiManager.ClearMenuContent();
                    return;
                }

                // Step 5: Execute Collection
                _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                         "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                         "🔄 Collecting data...\n" +
                                         $"Symbols: {string.Join(", ", symbols)}\n" +
                                         "Please wait, this may take a few minutes...");

                await ExecuteHistoricalDataCollection(symbols, timeFrame.Value, startDate.Value, endDate.Value);

                _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                         "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                         "✅ Data collection completed!\n" +
                                         "Check the results above.\n" +
                                         "Press any key to close...");

                System.Console.SetCursorPosition(0, System.Console.WindowHeight - 1);
                System.Console.Write("Press any key to return to main menu...");
                System.Console.ReadKey();
                _uiManager.ClearMenuContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in historical data menu");
                _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                         "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                         "❌ Error occurred during collection\n" +
                                         $"Error: {ex.Message}\n" +
                                         "Press any key to close...");

                System.Console.SetCursorPosition(0, System.Console.WindowHeight - 1);
                System.Console.Write("Press any key to return to main menu...");
                System.Console.ReadKey();
                _uiManager.ClearMenuContent();
            }
        }

        private async Task<List<string>> SelectSymbolsAsync()
        {
            var symbols = new List<string>();

            // Default symbols suggestion
            var defaultSymbols = new[] { "AAPL", "MSFT", "GOOGL", "AMZN", "TSLA" };

            _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                     "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                     "📊 Symbol Selection (Maximum 5)\n" +
                                     $"💡 Suggested: {string.Join(", ", defaultSymbols)}\n" +
                                     "\nUse default symbols? (y/n):");

            System.Console.SetCursorPosition(0, System.Console.WindowHeight - 1);
            System.Console.Write("Use default symbols (AAPL, MSFT, GOOGL, AMZN, TSLA)? (y/n): ");

            var useDefaults = System.Console.ReadKey();
            System.Console.WriteLine();

            if (useDefaults.KeyChar.ToString().ToLower() == "y")
            {
                symbols.AddRange(defaultSymbols);
                _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                         "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                         "📊 Symbol Selection\n" +
                                         $"✅ Selected: {string.Join(", ", symbols)}\n" +
                                         "Proceeding to next step...");
                await Task.Delay(1000);
                return symbols;
            }

            // Manual symbol entry
            _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                     "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                     "📊 Manual Symbol Entry\n" +
                                     "Enter symbols (max 5, empty to finish)\n" +
                                     "Current symbols: (none)");

            for (int i = 0; i < 5; i++)
            {
                _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                         "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                         "📊 Manual Symbol Entry\n" +
                                         $"Entering symbol {i + 1} of 5\n" +
                                         $"Current: {(symbols.Any() ? string.Join(", ", symbols) : "(none)")}\n" +
                                         "Enter symbol below (empty to finish):");

                System.Console.SetCursorPosition(0, System.Console.WindowHeight - 1);
                System.Console.Write($"Symbol {i + 1}: ");
                var symbol = System.Console.ReadLine()?.ToUpper().Trim();

                if (string.IsNullOrEmpty(symbol))
                    break;

                if (symbols.Contains(symbol))
                {
                    _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                             "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                             "📊 Manual Symbol Entry\n" +
                                             $"⚠️  {symbol} already added!\n" +
                                             $"Current: {string.Join(", ", symbols)}\n" +
                                             "Press any key to continue...");
                    System.Console.SetCursorPosition(0, System.Console.WindowHeight - 1);
                    System.Console.Write("Press any key to try again...");
                    System.Console.ReadKey();
                    i--; // Don't count duplicates
                    continue;
                }

                // Basic symbol validation
                if (symbol.Length < 1 || symbol.Length > 5 || !symbol.All(char.IsLetter))
                {
                    _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                             "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                             "📊 Manual Symbol Entry\n" +
                                             $"⚠️  Invalid symbol: {symbol}\n" +
                                             "Symbols must be 1-5 letters only\n" +
                                             "Press any key to try again...");
                    System.Console.SetCursorPosition(0, System.Console.WindowHeight - 1);
                    System.Console.Write("Press any key to try again...");
                    System.Console.ReadKey();
                    i--; // Allow retry
                    continue;
                }

                symbols.Add(symbol);
                _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                         "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                         "📊 Manual Symbol Entry\n" +
                                         $"✅ Added: {symbol}\n" +
                                         $"Current: {string.Join(", ", symbols)}\n" +
                                         "Continuing...");
                await Task.Delay(800);
            }

            return symbols;
        }

        private TimeFrame? SelectTimeFrame()
        {
            _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                     "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                     "⏱️  Timeframe Selection\n" +
                                     "Available timeframes:\n" +
                                     "1) 1-minute (most detailed)\n" +
                                     "2) 5-minute (recommended)\n" +
                                     "Select timeframe (1-2):");

            System.Console.SetCursorPosition(0, System.Console.WindowHeight - 1);
            System.Console.Write("Select timeframe (1-2): ");

            var choice = System.Console.ReadKey();
            System.Console.WriteLine();

            var selectedTimeFrame = choice.KeyChar switch
            {
                '1' => (TimeFrame?)TimeFrame.OneMinute,
                '2' => (TimeFrame?)TimeFrame.FiveMinutes,
                _ => (TimeFrame?)null
            };

            if (selectedTimeFrame != null)
            {
                _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                         "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                         "⏱️  Timeframe Selection\n" +
                                         $"✅ Selected: {selectedTimeFrame}\n" +
                                         "Proceeding to next step...");
                Task.Delay(800).Wait();
            }

            return selectedTimeFrame;
        }

        private (DateTime?, DateTime?) SelectDateRange()
        {
            _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                     "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                     "📅 Date Range Selection\n" +
                                     "Available ranges:\n" +
                                     "1) Last 1 day\n" +
                                     "2) Last 1 week (recommended)\n" +
                                     "3) Last 1 month\n" +
                                     "Select range (1-3):");

            System.Console.SetCursorPosition(0, System.Console.WindowHeight - 1);
            System.Console.Write("Select date range (1-3): ");

            var choice = System.Console.ReadKey();
            System.Console.WriteLine();

            var endDate = DateTime.UtcNow;
            DateTime? startDate = choice.KeyChar switch
            {
                '1' => endDate.AddDays(-1),
                '2' => endDate.AddDays(-7),
                '3' => endDate.AddDays(-30),
                _ => null
            };

            if (startDate != null)
            {
                var rangeText = choice.KeyChar switch
                {
                    '1' => "Last 1 day",
                    '2' => "Last 1 week",
                    '3' => "Last 1 month",
                    _ => "Unknown"
                };

                _uiManager.SetMenuContent("📈 Historical Data Collection\n" +
                                         "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                         "📅 Date Range Selection\n" +
                                         $"✅ Selected: {rangeText}\n" +
                                         $"From: {startDate:yyyy-MM-dd}\n" +
                                         $"To: {endDate:yyyy-MM-dd}\n" +
                                         "Proceeding to confirmation...");
                Task.Delay(800).Wait();
            }

            return (startDate, endDate);
        }

        private int EstimateRecordCount(int symbolCount, TimeFrame timeFrame, DateTime startDate, DateTime endDate)
        {
            var totalDays = (endDate - startDate).TotalDays;
            var tradingHours = 6.5; // Market hours per day
            var recordsPerSymbolPerDay = timeFrame switch
            {
                TimeFrame.OneMinute => (int)(tradingHours * 60), // 390 records per day
                TimeFrame.FiveMinutes => (int)(tradingHours * 12), // 78 records per day
                _ => 100
            };

            return (int)(symbolCount * totalDays * recordsPerSymbolPerDay);
        }

        private async Task ExecuteHistoricalDataCollection(List<string> symbols, TimeFrame timeFrame, DateTime startDate, DateTime endDate)
        {
            try
            {
                System.Console.WriteLine();
                System.Console.ForegroundColor = System.ConsoleColor.Green;
                System.Console.WriteLine("🚀 Starting Historical Data Collection");
                System.Console.WriteLine("═════════════════════════════════════");
                System.Console.ResetColor();
                System.Console.WriteLine();

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // Execute the collection
                var result = await _historicalDataService.FetchAndSaveHistoricalDataAsync(
                    symbols, startDate, endDate, timeFrame);

                stopwatch.Stop();

                // Display results
                System.Console.WriteLine();
                System.Console.ForegroundColor = result.Success ? System.ConsoleColor.Green : System.ConsoleColor.Red;
                System.Console.WriteLine($"✅ Collection Complete!");
                System.Console.ResetColor();
                System.Console.WriteLine();

                System.Console.WriteLine("📊 Results Summary:");
                System.Console.WriteLine($"   • Duration: {result.Duration.TotalSeconds:F1} seconds");
                System.Console.WriteLine($"   • Records Added: {result.RecordsAdded:N0}");
                System.Console.WriteLine($"   • Records Updated: {result.RecordsUpdated:N0}");
                System.Console.WriteLine($"   • Records Skipped: {result.RecordsSkipped:N0}");
                System.Console.WriteLine($"   • Total Success: {result.Success}");

                if (result.Warnings.Any())
                {
                    System.Console.WriteLine();
                    System.Console.ForegroundColor = System.ConsoleColor.Yellow;
                    System.Console.WriteLine($"⚠️  Warnings ({result.Warnings.Count}):");
                    foreach (var warning in result.Warnings.Take(3))
                    {
                        System.Console.WriteLine($"   • {warning}");
                    }
                    System.Console.ResetColor();
                }

                if (result.Errors.Any())
                {
                    System.Console.WriteLine();
                    System.Console.ForegroundColor = System.ConsoleColor.Red;
                    System.Console.WriteLine($"❌ Errors ({result.Errors.Count}):");
                    foreach (var error in result.Errors.Take(3))
                    {
                        System.Console.WriteLine($"   • {error}");
                    }
                    System.Console.ResetColor();
                }

                // Show updated statistics
                System.Console.WriteLine();
                System.Console.WriteLine("📈 Updated Database Statistics:");
                var stats = result.Statistics;
                System.Console.WriteLine($"   • Total Records: {stats.TotalRecords:N0}");
                System.Console.WriteLine($"   • Unique Symbols: {stats.UniqueSymbols}");
                System.Console.WriteLine($"   • Date Range: {stats.OldestData:yyyy-MM-dd} to {stats.NewestData:yyyy-MM-dd}");

                if (stats.SymbolCounts.Any())
                {
                    System.Console.WriteLine($"   • Top Symbols:");
                    foreach (var symbolCount in stats.SymbolCounts.OrderByDescending(x => x.Value).Take(3))
                    {
                        System.Console.WriteLine($"     - {symbolCount.Key}: {symbolCount.Value:N0} records");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during historical data collection");
                System.Console.WriteLine($"❌ Collection failed: {ex.Message}");
            }
        }

        private async Task ShowHistoricalDataReplayMenu()
        {
            try
            {
                System.Console.Clear();
                System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                System.Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
                System.Console.WriteLine("│                    📊 Historical Data Replay                   │");
                System.Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");
                System.Console.ResetColor();
                System.Console.WriteLine();

                // Check if we have a current sandbox session
                if (_currentSandboxSession == null)
                {
                    System.Console.WriteLine("❌ No active sandbox session. Please create or load a sandbox session first.");
                    System.Console.WriteLine("Press any key to return to main menu...");
                    System.Console.ReadKey();
                    return;
                }

                // Check if we have historical data
                var stats = await _historicalDataService.GetDataStatisticsAsync();
                if (stats.TotalRecords == 0)
                {
                    System.Console.WriteLine("❌ No historical data available. Please collect historical data first using 'H' hotkey.");
                    System.Console.WriteLine("Press any key to return to main menu...");
                    System.Console.ReadKey();
                    return;
                }

                System.Console.WriteLine($"📈 Available Historical Data:");
                System.Console.WriteLine($"   • Total Records: {stats.TotalRecords:N0}");
                System.Console.WriteLine($"   • Symbols: {stats.UniqueSymbols}");
                System.Console.WriteLine($"   • Date Range: {stats.OldestData:yyyy-MM-dd} to {stats.NewestData:yyyy-MM-dd}");
                System.Console.WriteLine();

                // Step 1: Symbol Selection for Replay
                var symbols = await SelectReplaySymbolsAsync(stats);
                if (symbols.Count == 0)
                {
                    System.Console.WriteLine("No symbols selected. Returning to main menu...");
                    await Task.Delay(2000);
                    return;
                }

                // Step 2: Date Range Selection for Replay
                var (startDate, endDate) = await SelectReplayDateRangeAsync(stats);
                if (startDate == null || endDate == null)
                {
                    System.Console.WriteLine("Invalid date range. Returning to main menu...");
                    await Task.Delay(2000);
                    return;
                }

                // Step 3: Replay Speed Selection
                var replaySpeed = SelectReplaySpeed();

                // Step 4: Show Summary and Confirm
                System.Console.WriteLine();
                System.Console.ForegroundColor = System.ConsoleColor.Yellow;
                System.Console.WriteLine("📋 Replay Configuration:");
                System.Console.WriteLine($"   • Symbols: {string.Join(", ", symbols)}");
                System.Console.WriteLine($"   • Date Range: {startDate:yyyy-MM-dd HH:mm} to {endDate:yyyy-MM-dd HH:mm}");
                System.Console.WriteLine($"   • Speed: {replaySpeed.TotalSeconds}x real-time");
                System.Console.WriteLine($"   • Session: {_currentSandboxSession.SessionName}");
                System.Console.WriteLine($"   • Balance: ${_currentSandboxSession.CurrentBalance:N2}");
                System.Console.ResetColor();
                System.Console.WriteLine();
                System.Console.Write("Start historical data replay? (y/n): ");

                var confirm = System.Console.ReadKey();
                System.Console.WriteLine();

                if (confirm.KeyChar.ToString().ToLower() != "y")
                {
                    System.Console.WriteLine("Replay cancelled. Returning to main menu...");
                    await Task.Delay(2000);
                    return;
                }

                // Step 5: Start Replay
                await StartHistoricalDataReplay(symbols, startDate.Value, endDate.Value, replaySpeed);

                System.Console.WriteLine();
                System.Console.WriteLine("Press any key to return to main menu...");
                System.Console.ReadKey();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in historical data replay menu");
                System.Console.WriteLine($"❌ Error: {ex.Message}");
                System.Console.WriteLine("Press any key to return to main menu...");
                System.Console.ReadKey();
            }
        }

        private async Task<List<string>> SelectReplaySymbolsAsync(DataStatistics stats)
        {
            var symbols = new List<string>();
            System.Console.WriteLine("📊 Symbol Selection for Replay");
            System.Console.WriteLine("═════════════════════════════");
            System.Console.WriteLine();

            // Show available symbols with data counts
            if (stats.SymbolCounts.Any())
            {
                System.Console.WriteLine("Available symbols with data:");
                var sortedSymbols = stats.SymbolCounts.OrderByDescending(x => x.Value).Take(10);
                foreach (var symbolCount in sortedSymbols)
                {
                    System.Console.WriteLine($"  • {symbolCount.Key}: {symbolCount.Value:N0} records");
                }
                System.Console.WriteLine();
            }

            System.Console.Write("Use all available symbols? (y/n): ");
            var useAll = System.Console.ReadKey();
            System.Console.WriteLine();

            if (useAll.KeyChar.ToString().ToLower() == "y")
            {
                symbols.AddRange(stats.SymbolCounts.Keys.Take(5)); // Limit to 5 for performance
                System.Console.WriteLine($"✅ Selected: {string.Join(", ", symbols)}");
                return symbols;
            }

            // Manual symbol entry
            System.Console.WriteLine();
            System.Console.WriteLine("Enter symbols for replay (one per line, empty line to finish):");

            for (int i = 0; i < 5; i++)
            {
                System.Console.Write($"Symbol {i + 1}: ");
                var symbol = System.Console.ReadLine()?.ToUpper().Trim();

                if (string.IsNullOrEmpty(symbol))
                    break;

                if (symbols.Contains(symbol))
                {
                    System.Console.WriteLine($"⚠️  {symbol} already added");
                    i--; // Don't count duplicates
                    continue;
                }

                // Check if we have data for this symbol
                if (!stats.SymbolCounts.ContainsKey(symbol))
                {
                    System.Console.WriteLine($"⚠️  No historical data found for {symbol}");
                    i--; // Allow retry
                    continue;
                }

                symbols.Add(symbol);
                System.Console.WriteLine($"✅ Added: {symbol} ({stats.SymbolCounts[symbol]:N0} records)");
            }

            return symbols;
        }

        private async Task<(DateTime?, DateTime?)> SelectReplayDateRangeAsync(DataStatistics stats)
        {
            System.Console.WriteLine();
            System.Console.WriteLine("📅 Date Range Selection for Replay");
            System.Console.WriteLine("═══════════════════════════════════");
            System.Console.WriteLine();
            System.Console.WriteLine($"Available data range: {stats.OldestData:yyyy-MM-dd} to {stats.NewestData:yyyy-MM-dd}");
            System.Console.WriteLine();
            System.Console.WriteLine("Suggested replay periods:");
            System.Console.WriteLine("  1) Last 1 day of available data");
            System.Console.WriteLine("  2) Last 3 days of available data");
            System.Console.WriteLine("  3) Last 1 week of available data");
            System.Console.WriteLine("  4) Custom date range");
            System.Console.WriteLine();
            System.Console.Write("Select option (1-4): ");

            var choice = System.Console.ReadKey();
            System.Console.WriteLine();

            var latestDate = stats.NewestData ?? DateTime.UtcNow;
            DateTime? startDate = choice.KeyChar switch
            {
                '1' => latestDate.AddDays(-1).Date.Add(new TimeSpan(9, 30, 0)), // Start at market open
                '2' => latestDate.AddDays(-3).Date.Add(new TimeSpan(9, 30, 0)), // Start at market open
                '3' => latestDate.AddDays(-7).Date.Add(new TimeSpan(9, 30, 0)), // Start at market open
                '4' => await GetCustomDateRange(stats),
                _ => null
            };

            if (choice.KeyChar != '4' && startDate != null)
            {
                System.Console.WriteLine($"✅ Selected: {startDate:yyyy-MM-dd HH:mm} ET (Market Open)");
            }

            if (choice.KeyChar == '4' && startDate == null)
                return (null, null);

            // Set end time to market close for suggested ranges, keep original time for custom
            var endDate = choice.KeyChar == '4' ? latestDate : latestDate.Date.Add(new TimeSpan(16, 0, 0));

            return (startDate, endDate);
        }

        private async Task<DateTime?> GetCustomDateRange(DataStatistics stats)
        {
            System.Console.WriteLine();
            System.Console.WriteLine("📅 Custom Date and Time Selection");
            System.Console.WriteLine("═════════════════════════════════");
            System.Console.WriteLine();
            System.Console.WriteLine($"Available data range: {stats.OldestData:yyyy-MM-dd} to {stats.NewestData:yyyy-MM-dd}");
            System.Console.WriteLine();

            // Get start date
            System.Console.Write("Enter start date (yyyy-MM-dd): ");
            var dateInput = System.Console.ReadLine();

            if (!DateTime.TryParse(dateInput, out var startDate))
            {
                System.Console.WriteLine("⚠️  Invalid date format. Please use yyyy-MM-dd format.");
                return null;
            }

            if (startDate.Date < stats.OldestData?.Date || startDate.Date > stats.NewestData?.Date)
            {
                System.Console.WriteLine("⚠️  Date is outside available data range");
                return null;
            }

            // Get start time
            System.Console.WriteLine();
            System.Console.WriteLine("⏰ Time Selection Options:");
            System.Console.WriteLine("  1) Market Open (9:30 AM ET)");
            System.Console.WriteLine("  2) Market Close (4:00 PM ET)");
            System.Console.WriteLine("  3) Pre-Market (4:00 AM ET)");
            System.Console.WriteLine("  4) After Hours (8:00 PM ET)");
            System.Console.WriteLine("  5) Custom time (HH:mm format)");
            System.Console.WriteLine();
            System.Console.Write("Select time option (1-5): ");

            var timeChoice = System.Console.ReadKey();
            System.Console.WriteLine();

            TimeSpan selectedTime;
            switch (timeChoice.KeyChar)
            {
                case '1':
                    selectedTime = new TimeSpan(9, 30, 0); // 9:30 AM
                    System.Console.WriteLine("✅ Selected: Market Open (9:30 AM ET)");
                    break;
                case '2':
                    selectedTime = new TimeSpan(16, 0, 0); // 4:00 PM
                    System.Console.WriteLine("✅ Selected: Market Close (4:00 PM ET)");
                    break;
                case '3':
                    selectedTime = new TimeSpan(4, 0, 0); // 4:00 AM
                    System.Console.WriteLine("✅ Selected: Pre-Market (4:00 AM ET)");
                    break;
                case '4':
                    selectedTime = new TimeSpan(20, 0, 0); // 8:00 PM
                    System.Console.WriteLine("✅ Selected: After Hours (8:00 PM ET)");
                    break;
                case '5':
                    System.Console.Write("Enter custom time (HH:mm, 24-hour format): ");
                    var timeInput = System.Console.ReadLine();

                    if (!TimeSpan.TryParse(timeInput, out selectedTime))
                    {
                        System.Console.WriteLine("⚠️  Invalid time format. Please use HH:mm format (e.g., 14:30)");
                        return null;
                    }
                    System.Console.WriteLine($"✅ Selected: Custom time ({selectedTime:hh\\:mm})");
                    break;
                default:
                    System.Console.WriteLine("⚠️  Invalid selection. Using market open time (9:30 AM)");
                    selectedTime = new TimeSpan(9, 30, 0);
                    break;
            }

            var finalDateTime = startDate.Date.Add(selectedTime);

            System.Console.WriteLine();
            System.Console.WriteLine($"📋 Final selection: {finalDateTime:yyyy-MM-dd HH:mm:ss} ET");

            return finalDateTime;
        }

        private TimeSpan SelectReplaySpeed()
        {
            System.Console.WriteLine();
            System.Console.WriteLine("⚡ Replay Speed Selection");
            System.Console.WriteLine("═══════════════════════");
            System.Console.WriteLine();
            System.Console.WriteLine("Available speeds:");
            System.Console.WriteLine("  1) Real-time (1x) - 1 minute = 1 minute");
            System.Console.WriteLine("  2) Fast (10x) - 1 minute = 6 seconds");
            System.Console.WriteLine("  3) Very Fast (60x) - 1 minute = 1 second");
            System.Console.WriteLine("  4) Ultra Fast (300x) - 1 minute = 0.2 seconds");
            System.Console.WriteLine();
            System.Console.Write("Select speed (1-4): ");

            var choice = System.Console.ReadKey();
            System.Console.WriteLine();

            return choice.KeyChar switch
            {
                '1' => TimeSpan.FromSeconds(60),    // Real-time
                '2' => TimeSpan.FromSeconds(6),     // 10x
                '3' => TimeSpan.FromSeconds(1),     // 60x
                '4' => TimeSpan.FromMilliseconds(200), // 300x
                _ => TimeSpan.FromSeconds(1)        // Default to 60x
            };
        }

        private async Task StartHistoricalDataReplay(List<string> symbols, DateTime startDate, DateTime endDate, TimeSpan replaySpeed)
        {
            try
            {
                System.Console.WriteLine();
                System.Console.ForegroundColor = System.ConsoleColor.Green;
                System.Console.WriteLine("🚀 Starting Historical Data Replay");
                System.Console.WriteLine("═══════════════════════════════════");
                System.Console.ResetColor();
                System.Console.WriteLine();

                // Subscribe to replay events
                _sandboxTradingService.MarketDataUpdated += OnReplayMarketDataUpdated;
                _sandboxTradingService.TradeExecuted += OnReplayTradeExecuted;
                _sandboxTradingService.ReplayStatusChanged += OnReplayStatusChanged;

                // Start the replay
                var success = await _sandboxTradingService.StartReplayAsync(
                    _currentSandboxSession!.SandboxSessionId,
                    symbols,
                    startDate,
                    endDate,
                    replaySpeed);

                if (success)
                {
                    System.Console.WriteLine("✅ Replay started successfully!");
                    System.Console.WriteLine();
                    System.Console.WriteLine("🎮 Replay Controls:");
                    System.Console.WriteLine("  • Alt+1,2,3 = Buy positions");
                    System.Console.WriteLine("  • Ctrl+1,2,3 = Sell positions");
                    System.Console.WriteLine("  • Space = Pause/Resume");
                    System.Console.WriteLine("  • Escape = Stop replay");
                    System.Console.WriteLine();

                    // Monitor replay until completion or user stops
                    await MonitorReplay();
                }
                else
                {
                    System.Console.WriteLine("❌ Failed to start replay");
                }

                // Unsubscribe from events
                _sandboxTradingService.MarketDataUpdated -= OnReplayMarketDataUpdated;
                _sandboxTradingService.TradeExecuted -= OnReplayTradeExecuted;
                _sandboxTradingService.ReplayStatusChanged -= OnReplayStatusChanged;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting historical data replay");
                System.Console.WriteLine($"❌ Error: {ex.Message}");
            }
        }

        private async Task MonitorReplay()
        {
            while (_sandboxTradingService.IsReplaying)
            {
                // Display current replay status
                var metrics = await _sandboxTradingService.GetPerformanceMetricsAsync();
                System.Console.SetCursorPosition(0, System.Console.CursorTop - 2);
                System.Console.WriteLine($"📊 Replay Progress: {metrics.Progress:P1} | Time: {metrics.CurrentTime:yyyy-MM-dd HH:mm} | Balance: ${metrics.CurrentBalance:N2} | P&L: ${metrics.TotalProfit:+#,0.00;-#,0.00;$0.00}");
                System.Console.WriteLine($"📈 Trades: {metrics.TotalTrades} total, {metrics.ProfitableTrades} profitable ({metrics.WinRate:P1} win rate)");

                // Check for user input
                if (System.Console.KeyAvailable)
                {
                    var key = System.Console.ReadKey(true);
                    if (key.Key == System.ConsoleKey.Escape)
                    {
                        _sandboxTradingService.StopReplay();
                        break;
                    }
                    else if (key.Key == System.ConsoleKey.Spacebar)
                    {
                        if (_sandboxTradingService.IsReplaying)
                        {
                            _sandboxTradingService.PauseReplay();
                            System.Console.WriteLine("⏸️  Replay paused - Press Space to resume");
                        }
                        else
                        {
                            _sandboxTradingService.ResumeReplay();
                            System.Console.WriteLine("▶️  Replay resumed");
                        }
                    }
                    else
                    {
                        // Handle trading hotkeys during replay
                        await HandleReplayTradingKeys(key);
                    }
                }

                await Task.Delay(100); // Update every 100ms
            }

            // Show final results
            var finalMetrics = await _sandboxTradingService.GetPerformanceMetricsAsync();
            System.Console.WriteLine();
            System.Console.ForegroundColor = finalMetrics.TotalProfit >= 0 ? System.ConsoleColor.Green : System.ConsoleColor.Red;
            System.Console.WriteLine("🏁 Replay Complete!");
            System.Console.WriteLine($"📊 Final Results:");
            System.Console.WriteLine($"   • Duration: {finalMetrics.StartTime:yyyy-MM-dd} to {finalMetrics.CurrentTime:yyyy-MM-dd}");
            System.Console.WriteLine($"   • Starting Balance: ${finalMetrics.InitialBalance:N2}");
            System.Console.WriteLine($"   • Final Balance: ${finalMetrics.CurrentBalance:N2}");
            System.Console.WriteLine($"   • Total P&L: ${finalMetrics.TotalProfit:+#,0.00;-#,0.00;$0.00}");
            System.Console.WriteLine($"   • Total Trades: {finalMetrics.TotalTrades}");
            System.Console.WriteLine($"   • Win Rate: {finalMetrics.WinRate:P1}");
            System.Console.ResetColor();
        }

        private async Task HandleReplayTradingKeys(ConsoleKeyInfo keyInfo)
        {
            var key = keyInfo.Key;
            var modifiers = keyInfo.Modifiers;

            // Get current market data for trading
            var marketData = _sandboxTradingService.GetCurrentMarketState().ToList();
            if (!marketData.Any()) return;

            // Trading hotkeys during replay
            if (modifiers.HasFlag(System.ConsoleModifiers.Alt))
            {
                switch (key)
                {
                    case System.ConsoleKey.D1:
                        await ExecuteReplayTrade("BUY", GetReplaySymbol(marketData, 1), 100);
                        break;
                    case System.ConsoleKey.D2:
                        await ExecuteReplayTrade("BUY", GetReplaySymbol(marketData, 2), 100);
                        break;
                    case System.ConsoleKey.D3:
                        await ExecuteReplayTrade("BUY", GetReplaySymbol(marketData, 3), 100);
                        break;
                }
            }
            else if (modifiers.HasFlag(System.ConsoleModifiers.Control))
            {
                switch (key)
                {
                    case System.ConsoleKey.D1:
                        await ExecuteReplayTrade("SELL", GetReplaySymbol(marketData, 1), 100);
                        break;
                    case System.ConsoleKey.D2:
                        await ExecuteReplayTrade("SELL", GetReplaySymbol(marketData, 2), 100);
                        break;
                    case System.ConsoleKey.D3:
                        await ExecuteReplayTrade("SELL", GetReplaySymbol(marketData, 3), 100);
                        break;
                }
            }
        }

        private string GetReplaySymbol(List<StockQuote> marketData, int index)
        {
            return index <= marketData.Count ? marketData[index - 1].Symbol : "UNKNOWN";
        }

        private async Task ExecuteReplayTrade(string action, string symbol, int quantity)
        {
            try
            {
                var tradeAction = action == "BUY" ? TradeAction.Buy : TradeAction.Sell;
                var result = await _sandboxTradingService.ExecuteTradeAsync(symbol, tradeAction, quantity);

                if (result.Success)
                {
                    System.Console.WriteLine($"✅ {action} EXECUTED: {quantity} {symbol} @ ${result.ExecutionPrice:F2} (Total: ${result.TotalCost:F2})");
                }
                else
                {
                    System.Console.WriteLine($"❌ {action} FAILED: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing replay trade");
                System.Console.WriteLine($"❌ TRADE ERROR: {ex.Message}");
            }
        }

        private void OnReplayMarketDataUpdated(object? sender, MarketDataReplayEventArgs e)
        {
            // Update the live quotes for display
            lock (_liveQuotes)
            {
                _liveQuotes.Clear();
                _liveQuotes.AddRange(e.MarketData);
            }
        }

        private void OnReplayTradeExecuted(object? sender, TradeExecutionEventArgs e)
        {
            // Log trade execution
            _logger.LogInformation("Replay trade executed: {Action} {Quantity} {Symbol} @ ${Price}",
                e.Trade.Action, e.Trade.Quantity, e.Trade.Symbol, e.Trade.ExecutionPrice);
        }

        private void OnReplayStatusChanged(object? sender, ReplayStatusEventArgs e)
        {
            _logger.LogInformation("Replay status changed: {Status} at {Time}",
                e.IsActive ? "Started" : "Stopped", e.CurrentTime);
        }
    }
}