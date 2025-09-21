using Microsoft.Extensions.Logging;
using VBTrader.Core.Models;
using VBTrader.Core.Interfaces;

namespace VBTrader.Console.UI;

public class ConsoleUIManager
{
    private readonly ILogger<ConsoleUIManager> _logger;
    private readonly object _lockObject = new();
    private int _windowWidth;
    private int _windowHeight;
    private bool _isInitialized;

    // Panel dimensions
    private readonly int _headerHeight = 6;
    private readonly int _statusHeight = 2;
    private readonly int _quotesHeight = 10;
    private readonly int _opportunitiesHeight = 8;
    private readonly int _instructionsHeight = 18; // Space for all hotkey instructions
    private readonly int _menuAreaHeight = 8; // Space for menu/dialog area

    // UI state
    public bool ShowInstructions => _showInstructions; // Public property for external access
    private bool _showInstructions = true; // Instructions visible by default
    private string _currentMenuContent = string.Empty; // Content for menu area

    public ConsoleUIManager(ILogger<ConsoleUIManager> logger)
    {
        _logger = logger;
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            System.Console.CursorVisible = false;
            _windowWidth = System.Console.WindowWidth;
            _windowHeight = System.Console.WindowHeight;
            _isInitialized = true;
            _logger.LogInformation("ConsoleUIManager initialized with dimensions {Width}x{Height}", _windowWidth, _windowHeight);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Unable to initialize console UI properly: {Message}", ex.Message);
            _windowWidth = 80;
            _windowHeight = 25;
            _isInitialized = false;
        }
    }

    public void Clear()
    {
        lock (_lockObject)
        {
            try
            {
                System.Console.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Unable to clear console: {Message}", ex.Message);
            }
        }
    }

    public void DrawHeader(User? currentUser, bool sandboxMode, SandboxSession? sandboxSession, bool schwabConnected)
    {
        lock (_lockObject)
        {
            try
            {
                SetCursorPosition(0, 0);

                System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                WriteHorizontalLine('═', "VBTrader - Real-Time Trading System");

                // User and connection info
                if (currentUser != null)
                {
                    var userInfo = $"User: {currentUser.Username}";
                    var connectionInfo = schwabConnected ? "🟢 SCHWAB CONNECTED" : "⚠️  SIMULATED DATA";
                    var statusLine = $"║ {userInfo.PadRight(25)} {connectionInfo.PadLeft(_windowWidth - 30)} ║";
                    System.Console.WriteLine(statusLine.Substring(0, Math.Min(statusLine.Length, _windowWidth)));
                }

                // Trading mode indicator
                if (sandboxMode)
                {
                    System.Console.ForegroundColor = System.ConsoleColor.Yellow;
                    var modeLine = $"║                            ⚠️  SANDBOX MODE  ⚠️                             ║";
                    System.Console.WriteLine(modeLine.Substring(0, Math.Min(modeLine.Length, _windowWidth)));

                    if (sandboxSession != null)
                    {
                        var sessionInfo = $"Session: {sandboxSession.SessionName} | Balance: ${sandboxSession.CurrentBalance:N2}";
                        var sessionLine = $"║ {sessionInfo.PadRight(_windowWidth - 4)} ║";
                        System.Console.WriteLine(sessionLine.Substring(0, Math.Min(sessionLine.Length, _windowWidth)));
                    }
                }
                else
                {
                    System.Console.ForegroundColor = System.ConsoleColor.Green;
                    var modeLine = $"║                               🟢  LIVE MODE  🟢                              ║";
                    System.Console.WriteLine(modeLine.Substring(0, Math.Min(modeLine.Length, _windowWidth)));
                }

                System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                WriteHorizontalLine('═');
                System.Console.ResetColor();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error drawing header: {Message}", ex.Message);
            }
        }
    }

    public void DrawQuotesPanel(IEnumerable<StockQuote> quotes)
    {
        lock (_lockObject)
        {
            try
            {
                var startY = _headerHeight;
                SetCursorPosition(0, startY);

                var mainPanelWidth = _windowWidth - 65; // Leave space for instructions panel
                if (mainPanelWidth < 50) mainPanelWidth = _windowWidth; // Fallback for small screens

                System.Console.ForegroundColor = System.ConsoleColor.White;
                System.Console.WriteLine("REAL-TIME QUOTES:");
                WriteHorizontalLine('═', null, mainPanelWidth);

                System.Console.ForegroundColor = System.ConsoleColor.Gray;
                var headerLine = "Symbol    Price      Change     Change%    Volume";
                System.Console.WriteLine(headerLine.PadRight(mainPanelWidth));
                WriteHorizontalLine('─', null, mainPanelWidth);

                var quotesToShow = quotes.Take(_quotesHeight - 4);
                foreach (var quote in quotesToShow)
                {
                    System.Console.ForegroundColor = quote.Change >= 0 ? System.ConsoleColor.Green : System.ConsoleColor.Red;
                    var quoteLine = $"{quote.Symbol,-8} ${quote.LastPrice,8:F2} {quote.Change,8:F2} {quote.ChangePercent,8:F2}% {quote.Volume,12:N0}";
                    System.Console.WriteLine(quoteLine.Substring(0, Math.Min(quoteLine.Length, mainPanelWidth)));
                }

                // Fill remaining lines in quotes panel
                var currentY = System.Console.CursorTop;
                var targetY = startY + _quotesHeight;
                while (currentY < targetY && currentY < _windowHeight - _statusHeight - 1)
                {
                    System.Console.WriteLine(new string(' ', mainPanelWidth));
                    currentY++;
                }

                System.Console.ResetColor();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error drawing quotes panel: {Message}", ex.Message);
            }
        }
    }

    public void DrawOpportunitiesPanel(IEnumerable<MarketOpportunity> opportunities)
    {
        lock (_lockObject)
        {
            try
            {
                var startY = _headerHeight + _quotesHeight;
                SetCursorPosition(0, startY);

                var mainPanelWidth = _windowWidth - 65; // Leave space for instructions panel
                if (mainPanelWidth < 50) mainPanelWidth = _windowWidth; // Fallback for small screens

                System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                System.Console.WriteLine("MARKET OPPORTUNITIES:");
                WriteHorizontalLine('═', null, mainPanelWidth);

                System.Console.ForegroundColor = System.ConsoleColor.Gray;
                var headerLine = "Symbol  Score  Type                 Change%";
                System.Console.WriteLine(headerLine.PadRight(mainPanelWidth));
                WriteHorizontalLine('─', null, mainPanelWidth);

                var oppsToShow = opportunities.Take(_opportunitiesHeight - 4);
                foreach (var opp in oppsToShow)
                {
                    System.Console.ForegroundColor = opp.Score > 80 ? System.ConsoleColor.Yellow : System.ConsoleColor.White;
                    var oppLine = $"{opp.Symbol,-6} {opp.Score,6:F1}  {opp.OpportunityType,-18} {opp.PriceChangePercent,6:F2}%";
                    System.Console.WriteLine(oppLine.Substring(0, Math.Min(oppLine.Length, mainPanelWidth)));
                }

                // Fill remaining lines in opportunities panel
                var currentY = System.Console.CursorTop;
                var targetY = startY + _opportunitiesHeight;
                while (currentY < targetY && currentY < _windowHeight - _statusHeight - 1)
                {
                    System.Console.WriteLine(new string(' ', mainPanelWidth));
                    currentY++;
                }

                System.Console.ResetColor();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error drawing opportunities panel: {Message}", ex.Message);
            }
        }
    }

    public void DrawPositionsPanel(Dictionary<string, decimal> positions, IEnumerable<StockQuote> quotes)
    {
        lock (_lockObject)
        {
            try
            {
                var startY = _headerHeight + _quotesHeight + _opportunitiesHeight;
                var availableHeight = _windowHeight - startY - _statusHeight - 1;

                if (availableHeight <= 0) return;

                SetCursorPosition(0, startY);

                var mainPanelWidth = _windowWidth - 65; // Leave space for instructions panel
                if (mainPanelWidth < 50) mainPanelWidth = _windowWidth; // Fallback for small screens

                System.Console.ForegroundColor = System.ConsoleColor.Magenta;
                System.Console.WriteLine("CURRENT POSITIONS:");
                WriteHorizontalLine('═', null, mainPanelWidth);

                if (positions.Any())
                {
                    System.Console.ForegroundColor = System.ConsoleColor.Gray;
                    var headerLine = "Symbol  Shares     Market Value";
                    System.Console.WriteLine(headerLine.PadRight(mainPanelWidth));
                    WriteHorizontalLine('─', null, mainPanelWidth);

                    var positionsToShow = positions.Take(availableHeight - 4);
                    foreach (var position in positionsToShow)
                    {
                        var quote = quotes.FirstOrDefault(q => q.Symbol == position.Key);
                        var marketValue = position.Value * (quote?.LastPrice ?? 0);
                        System.Console.ForegroundColor = System.ConsoleColor.White;
                        var positionLine = $"{position.Key,-6} {position.Value,8:F0}     ${marketValue,10:F2}";
                        System.Console.WriteLine(positionLine.Substring(0, Math.Min(positionLine.Length, mainPanelWidth)));
                    }
                }
                else
                {
                    System.Console.ForegroundColor = System.ConsoleColor.Gray;
                    System.Console.WriteLine("No positions held.".PadRight(mainPanelWidth));
                }

                System.Console.ResetColor();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error drawing positions panel: {Message}", ex.Message);
            }
        }
    }

    public void DrawStatusBar(string status = "")
    {
        lock (_lockObject)
        {
            try
            {
                var statusY = _windowHeight - _statusHeight;
                SetCursorPosition(0, statusY);

                System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var defaultStatus = $"Last Update: {timestamp} | Market: OPEN | Press 'Q' to quit, 'H' for historical data";
                var displayStatus = string.IsNullOrEmpty(status) ? defaultStatus : status;

                System.Console.WriteLine(displayStatus.PadRight(_windowWidth).Substring(0, _windowWidth));
                System.Console.ResetColor();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error drawing status bar: {Message}", ex.Message);
            }
        }
    }

    public void DrawInstructionsPanel()
    {
        lock (_lockObject)
        {
            try
            {
                if (!_showInstructions) return; // Skip drawing if toggled off
                // Calculate the starting position for instructions (right side of screen)
                var instructionsX = _windowWidth - 60; // Right side with 60 char width
                var instructionsY = _headerHeight + 1;

                if (instructionsX < 0) instructionsX = 0; // Fallback for small screens

                System.Console.ForegroundColor = System.ConsoleColor.Yellow;

                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("╔══════════════════════════════════════════════════════════╗");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║                         HOTKEYS                         ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("╠══════════════════════════════════════════════════════════╣");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ Alt+1,2,3   = Buy 100 shares of stocks 1,2,3           ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ Ctrl+1,2,3  = Sell 100 shares of stocks 1,2,3          ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ R           = Refresh data                              ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ Q           = Quit                                      ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ S           = Set stock symbols                         ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ Ctrl+T      = Toggle Sandbox/Live mode                 ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ N           = Create new sandbox session               ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ L           = Load existing sandbox session            ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ C           = Configure Schwab API credentials         ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ H           = Historical data collection (sandbox only) ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ P           = Historical data replay (sandbox only)    ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ F1          = Cycle console log level                  ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ F2          = Cycle file log level                     ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ F3          = Toggle console logging on/off            ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ F4          = Toggle file logging on/off               ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ F5          = Show current logging status              ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("║ Ctrl+H      = Toggle this hotkeys panel               ║");
                SetCursorPosition(instructionsX, instructionsY++);
                System.Console.Write("╚══════════════════════════════════════════════════════════╝");

                System.Console.ResetColor();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error drawing instructions panel: {Message}", ex.Message);
            }
        }
    }

    public void ShowMessage(string message, System.ConsoleColor color = System.ConsoleColor.White, int durationMs = 3000)
    {
        lock (_lockObject)
        {
            try
            {
                var originalColor = System.Console.ForegroundColor;
                System.Console.ForegroundColor = color;

                // Show message in status area
                var statusY = _windowHeight - _statusHeight;
                SetCursorPosition(0, statusY);
                System.Console.WriteLine(message.PadRight(_windowWidth).Substring(0, _windowWidth));

                System.Console.ForegroundColor = originalColor;

                // Optional: Auto-clear after duration
                if (durationMs > 0)
                {
                    Task.Delay(durationMs).ContinueWith(_ => DrawStatusBar());
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error showing message: {Message}", ex.Message);
            }
        }
    }

    public void RefreshLayout(
        User? currentUser,
        bool sandboxMode,
        SandboxSession? sandboxSession,
        bool schwabConnected,
        IEnumerable<StockQuote> quotes,
        IEnumerable<MarketOpportunity> opportunities,
        Dictionary<string, decimal> positions)
    {
        lock (_lockObject)
        {
            try
            {
                // Check if console dimensions changed
                if (_isInitialized &&
                    (System.Console.WindowWidth != _windowWidth || System.Console.WindowHeight != _windowHeight))
                {
                    _windowWidth = System.Console.WindowWidth;
                    _windowHeight = System.Console.WindowHeight;
                    Clear();
                }

                DrawHeader(currentUser, sandboxMode, sandboxSession, schwabConnected);
                DrawQuotesPanel(quotes);
                DrawOpportunitiesPanel(opportunities);
                DrawPositionsPanel(positions, quotes);
                DrawMenuArea(); // Draw menu area below positions
                DrawInstructionsPanel(); // Add instructions to main refresh cycle
                DrawStatusBar();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing UI layout");
            }
        }
    }

    private void SetCursorPosition(int left, int top)
    {
        try
        {
            if (_isInitialized && left >= 0 && top >= 0 && left < _windowWidth && top < _windowHeight)
            {
                System.Console.SetCursorPosition(left, top);
            }
        }
        catch (Exception)
        {
            // Ignore cursor positioning errors
        }
    }

    private void WriteHorizontalLine(char character, string? title = null, int? width = null)
    {
        try
        {
            var lineWidth = width ?? _windowWidth;

            if (string.IsNullOrEmpty(title))
            {
                System.Console.WriteLine(new string(character, lineWidth));
            }
            else
            {
                var padding = (lineWidth - title.Length - 4) / 2;
                var line = $"{character}{new string(character, padding)} {title} {new string(character, lineWidth - padding - title.Length - 4)}{character}";
                System.Console.WriteLine(line.Substring(0, Math.Min(line.Length, lineWidth)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error writing horizontal line: {Message}", ex.Message);
        }
    }

    public void ToggleInstructions()
    {
        _showInstructions = !_showInstructions;
        _logger.LogDebug("Instructions panel toggled to: {Visible}", _showInstructions);
    }

    public void SetMenuContent(string content)
    {
        _currentMenuContent = content ?? string.Empty;
        _logger.LogDebug("Menu content set: {HasContent}", !string.IsNullOrEmpty(content));
    }

    public void ClearMenuContent()
    {
        _currentMenuContent = string.Empty;
    }

    public void DrawMenuArea()
    {
        lock (_lockObject)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentMenuContent)) return;

                var startY = _headerHeight + _quotesHeight + _opportunitiesHeight + 6; // Below positions
                var availableHeight = _windowHeight - startY - _statusHeight - 1;

                if (availableHeight <= 0) return;

                SetCursorPosition(0, startY);

                var mainPanelWidth = _windowWidth - (_showInstructions ? 65 : 5);
                if (mainPanelWidth < 50) mainPanelWidth = _windowWidth;

                // Draw menu border
                System.Console.ForegroundColor = System.ConsoleColor.Yellow;
                System.Console.WriteLine("┌" + new string('─', mainPanelWidth - 2) + "┐");

                // Draw menu content
                System.Console.ForegroundColor = System.ConsoleColor.White;
                var lines = _currentMenuContent.Split('\n');
                var maxLines = Math.Min(lines.Length, availableHeight - 2);

                for (int i = 0; i < maxLines; i++)
                {
                    var line = lines[i];
                    if (line.Length > mainPanelWidth - 4)
                        line = line.Substring(0, mainPanelWidth - 4);

                    System.Console.WriteLine("│ " + line.PadRight(mainPanelWidth - 3) + "│");
                }

                // Fill remaining space and close border
                for (int i = maxLines; i < Math.Min(_menuAreaHeight - 2, availableHeight - 2); i++)
                {
                    System.Console.WriteLine("│" + new string(' ', mainPanelWidth - 2) + "│");
                }

                System.Console.ForegroundColor = System.ConsoleColor.Yellow;
                System.Console.WriteLine("└" + new string('─', mainPanelWidth - 2) + "┘");
                System.Console.ResetColor();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error drawing menu area: {Message}", ex.Message);
            }
        }
    }

    public void Dispose()
    {
        try
        {
            System.Console.CursorVisible = true;
            System.Console.ResetColor();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error disposing ConsoleUIManager: {Message}", ex.Message);
        }
    }
}