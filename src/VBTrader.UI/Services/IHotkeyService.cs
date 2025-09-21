using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using VBTrader.Core.Models;
using Windows.System;

namespace VBTrader.UI.Services;

public interface IHotkeyService
{
    event EventHandler<HotkeyEventArgs>? HotkeyPressed;
    Task RegisterHotkeysAsync(TradingHotkeys hotkeys);
    Task UnregisterAllHotkeysAsync();
    bool IsEnabled { get; set; }
}

public class HotkeyEventArgs : EventArgs
{
    public TradingAction Action { get; set; }
    public int StockIndex { get; set; }
    public int Quantity { get; set; }
    public bool IsMaxPosition { get; set; }

    public HotkeyEventArgs(TradingAction action, int stockIndex, int quantity, bool isMaxPosition = false)
    {
        Action = action;
        StockIndex = stockIndex;
        Quantity = quantity;
        IsMaxPosition = isMaxPosition;
    }
}

public enum TradingAction
{
    Buy,
    Sell
}

public class HotkeyService : IHotkeyService, IDisposable
{
    public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

    private readonly Dictionary<int, (TradingAction Action, int StockIndex, int Quantity, bool IsMax)> _registeredHotkeys = new();
    private int _nextHotkeyId = 1000;
    private bool _isEnabled = true;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public async Task RegisterHotkeysAsync(TradingHotkeys hotkeys)
    {
        await UnregisterAllHotkeysAsync();

        if (!hotkeys.DefaultAmounts.EnableHotkeys)
            return;

        try
        {
            // Stock 1 hotkeys
            RegisterHotkey(hotkeys.Stock1Buy100, TradingAction.Buy, 1, hotkeys.DefaultAmounts.SmallQuantity);
            RegisterHotkey(hotkeys.Stock1Sell100, TradingAction.Sell, 1, hotkeys.DefaultAmounts.SmallQuantity);
            RegisterHotkey(hotkeys.Stock1Buy1000, TradingAction.Buy, 1, hotkeys.DefaultAmounts.LargeQuantity);
            RegisterHotkey(hotkeys.Stock1SellAll, TradingAction.Sell, 1, 0, true);

            // Stock 2 hotkeys
            RegisterHotkey(hotkeys.Stock2Buy100, TradingAction.Buy, 2, hotkeys.DefaultAmounts.SmallQuantity);
            RegisterHotkey(hotkeys.Stock2Sell100, TradingAction.Sell, 2, hotkeys.DefaultAmounts.SmallQuantity);
            RegisterHotkey(hotkeys.Stock2Buy1000, TradingAction.Buy, 2, hotkeys.DefaultAmounts.LargeQuantity);
            RegisterHotkey(hotkeys.Stock2SellAll, TradingAction.Sell, 2, 0, true);

            // Stock 3 hotkeys
            RegisterHotkey(hotkeys.Stock3Buy100, TradingAction.Buy, 3, hotkeys.DefaultAmounts.SmallQuantity);
            RegisterHotkey(hotkeys.Stock3Sell100, TradingAction.Sell, 3, hotkeys.DefaultAmounts.SmallQuantity);
            RegisterHotkey(hotkeys.Stock3Buy1000, TradingAction.Buy, 3, hotkeys.DefaultAmounts.LargeQuantity);
            RegisterHotkey(hotkeys.Stock3SellAll, TradingAction.Sell, 3, 0, true);
        }
        catch (Exception ex)
        {
            // Log error - some hotkeys might already be registered by other applications
            System.Diagnostics.Debug.WriteLine($"Error registering hotkeys: {ex.Message}");
        }
    }

    private void RegisterHotkey(KeyCombination keyCombination, TradingAction action, int stockIndex, int quantity, bool isMax = false)
    {
        var modifiers = GetNativeModifiers(keyCombination.Modifiers);
        var vk = GetVirtualKey(keyCombination.Key);

        var hotkeyId = _nextHotkeyId++;

        if (RegisterHotKey(IntPtr.Zero, hotkeyId, modifiers, vk))
        {
            _registeredHotkeys[hotkeyId] = (action, stockIndex, quantity, isMax);
        }
    }

    public async Task UnregisterAllHotkeysAsync()
    {
        foreach (var hotkeyId in _registeredHotkeys.Keys)
        {
            UnregisterHotKey(IntPtr.Zero, hotkeyId);
        }
        _registeredHotkeys.Clear();
        await Task.CompletedTask;
    }

    private uint GetNativeModifiers(System.Windows.Input.ModifierKeys modifiers)
    {
        uint nativeModifiers = 0;

        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt))
            nativeModifiers |= 0x0001; // MOD_ALT

        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
            nativeModifiers |= 0x0002; // MOD_CONTROL

        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
            nativeModifiers |= 0x0004; // MOD_SHIFT

        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows))
            nativeModifiers |= 0x0008; // MOD_WIN

        return nativeModifiers;
    }

    private uint GetVirtualKey(System.Windows.Input.Key key)
    {
        return key switch
        {
            System.Windows.Input.Key.D1 => 0x31, // '1'
            System.Windows.Input.Key.D2 => 0x32, // '2'
            System.Windows.Input.Key.D3 => 0x33, // '3'
            System.Windows.Input.Key.D4 => 0x34, // '4'
            System.Windows.Input.Key.D5 => 0x35, // '5'
            System.Windows.Input.Key.D6 => 0x36, // '6'
            System.Windows.Input.Key.D7 => 0x37, // '7'
            System.Windows.Input.Key.D8 => 0x38, // '8'
            System.Windows.Input.Key.D9 => 0x39, // '9'
            System.Windows.Input.Key.D0 => 0x30, // '0'
            _ => 0x31 // Default to '1'
        };
    }

    // Simulate hotkey press for testing
    public void SimulateHotkeyPress(TradingAction action, int stockIndex, int quantity, bool isMax = false)
    {
        if (!_isEnabled) return;

        var args = new HotkeyEventArgs(action, stockIndex, quantity, isMax);
        HotkeyPressed?.Invoke(this, args);
    }

    public void Dispose()
    {
        UnregisterAllHotkeysAsync().Wait();
    }

    // Windows API functions for global hotkeys (simplified for WinUI)
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}