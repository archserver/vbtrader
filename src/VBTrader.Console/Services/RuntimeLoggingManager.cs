using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;

namespace VBTrader.Console.Services;

public class RuntimeLoggingManager : IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly LoggingLevelSwitch _consoleLoggingLevel;
    private readonly LoggingLevelSwitch _fileLoggingLevel;
    private readonly Dictionary<string, LogLevel> _logLevelMap;

    public LogLevel CurrentConsoleLevel { get; private set; }
    public LogLevel CurrentFileLevel { get; private set; }
    public bool LogToFile { get; set; }
    public bool LogToConsole { get; set; }

    public RuntimeLoggingManager(IConfiguration configuration)
    {
        _configuration = configuration;
        _consoleLoggingLevel = new LoggingLevelSwitch();
        _fileLoggingLevel = new LoggingLevelSwitch();

        _logLevelMap = new Dictionary<string, LogLevel>
        {
            { "trace", LogLevel.Trace },
            { "debug", LogLevel.Debug },
            { "information", LogLevel.Information },
            { "warning", LogLevel.Warning },
            { "error", LogLevel.Error },
            { "critical", LogLevel.Critical },
            { "none", LogLevel.None }
        };

        InitializeFromConfiguration();
    }

    private void InitializeFromConfiguration()
    {
        var loggingConfig = _configuration.GetSection("VBTraderLogging");

        var consoleLevel = loggingConfig.GetValue<string>("ConsoleLogLevel", "Error");
        var fileLevel = loggingConfig.GetValue<string>("FileLogLevel", "Debug");

        LogToFile = loggingConfig.GetValue<bool>("LogToFile", true);
        LogToConsole = loggingConfig.GetValue<bool>("LogToConsole", true);

        SetConsoleLogLevel(consoleLevel);
        SetFileLogLevel(fileLevel);
    }

    public void SetConsoleLogLevel(string level)
    {
        if (_logLevelMap.TryGetValue(level.ToLowerInvariant(), out var logLevel))
        {
            CurrentConsoleLevel = logLevel;
            _consoleLoggingLevel.MinimumLevel = ConvertToSerilogLevel(logLevel);
        }
    }

    public void SetFileLogLevel(string level)
    {
        if (_logLevelMap.TryGetValue(level.ToLowerInvariant(), out var logLevel))
        {
            CurrentFileLevel = logLevel;
            _fileLoggingLevel.MinimumLevel = ConvertToSerilogLevel(logLevel);
        }
    }

    public void CycleConsoleLogLevel()
    {
        var levels = new[] { LogLevel.Error, LogLevel.Warning, LogLevel.Information, LogLevel.Debug };
        var currentIndex = Array.IndexOf(levels, CurrentConsoleLevel);
        var nextIndex = (currentIndex + 1) % levels.Length;

        var nextLevel = levels[nextIndex];
        SetConsoleLogLevel(nextLevel.ToString());

        System.Console.WriteLine($"Console log level changed to: {nextLevel}");
    }

    public void CycleFileLogLevel()
    {
        var levels = new[] { LogLevel.Error, LogLevel.Warning, LogLevel.Information, LogLevel.Debug };
        var currentIndex = Array.IndexOf(levels, CurrentFileLevel);
        var nextIndex = (currentIndex + 1) % levels.Length;

        var nextLevel = levels[nextIndex];
        SetFileLogLevel(nextLevel.ToString());

        System.Console.WriteLine($"File log level changed to: {nextLevel}");
    }

    public void ToggleFileLogging()
    {
        LogToFile = !LogToFile;
        ReconfigureSerilog();
        System.Console.WriteLine($"File logging: {(LogToFile ? "Enabled" : "Disabled")}");
    }

    public void ToggleConsoleLogging()
    {
        LogToConsole = !LogToConsole;
        ReconfigureSerilog();
        System.Console.WriteLine($"Console logging: {(LogToConsole ? "Enabled" : "Disabled")}");
    }

    public string GetCurrentStatus()
    {
        return $"Console: {CurrentConsoleLevel} | File: {CurrentFileLevel} | " +
               $"Console Output: {(LogToConsole ? "On" : "Off")} | File Output: {(LogToFile ? "On" : "Off")}";
    }

    public LoggingLevelSwitch GetConsoleLoggingSwitch() => _consoleLoggingLevel;
    public LoggingLevelSwitch GetFileLoggingSwitch() => _fileLoggingLevel;

    private static Serilog.Events.LogEventLevel ConvertToSerilogLevel(LogLevel logLevel)
    {
        return logLevel switch
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
    }

    private void ReconfigureSerilog()
    {
        // Close the existing logger before creating a new one
        Log.CloseAndFlush();

        var loggerConfig = new LoggerConfiguration();

        // Set minimum level to the most restrictive of console or file
        var minimumLevel = LogToConsole && LogToFile
            ? (CurrentConsoleLevel < CurrentFileLevel ? CurrentConsoleLevel : CurrentFileLevel)
            : LogToConsole ? CurrentConsoleLevel
            : LogToFile ? CurrentFileLevel
            : LogLevel.Error;

        loggerConfig.MinimumLevel.Is(ConvertToSerilogLevel(minimumLevel));

        // Override minimum levels for Microsoft namespaces - be more aggressive
        var consoleLogLevel = ConvertToSerilogLevel(CurrentConsoleLevel);
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

        if (LogToConsole)
        {
            // Use a more restrictive console sink when level is Error - only show Error and Fatal
            var restrictiveConsoleLevel = CurrentConsoleLevel == LogLevel.Error
                ? Serilog.Events.LogEventLevel.Error
                : ConvertToSerilogLevel(CurrentConsoleLevel);

            loggerConfig.WriteTo.Console(
                restrictedToMinimumLevel: restrictiveConsoleLevel,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
        }

        if (LogToFile)
        {
            var logPath = _configuration.GetValue<string>("Logging:File:Path", "logs/vbtrader-{Date}.log");
            loggerConfig.WriteTo.File(
                path: logPath,
                levelSwitch: _fileLoggingLevel,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10_485_760,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
        }

        Log.Logger = loggerConfig.CreateLogger();
    }

    public void Dispose()
    {
        Log.CloseAndFlush();
    }
}