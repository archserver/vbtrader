namespace VBTrader.Core.Models;

public class TradingHotkeys
{
    // Stock 1 Hotkeys
    public KeyCombination Stock1Buy100 { get; set; } = new(VirtualKey.Number1, ModifierKeys.Alt);
    public KeyCombination Stock1Sell100 { get; set; } = new(VirtualKey.Number1, ModifierKeys.Control);
    public KeyCombination Stock1Buy1000 { get; set; } = new(VirtualKey.Number1, ModifierKeys.Alt | ModifierKeys.Shift);
    public KeyCombination Stock1SellAll { get; set; } = new(VirtualKey.Number1, ModifierKeys.Control | ModifierKeys.Shift);

    // Stock 2 Hotkeys
    public KeyCombination Stock2Buy100 { get; set; } = new(VirtualKey.Number2, ModifierKeys.Alt);
    public KeyCombination Stock2Sell100 { get; set; } = new(VirtualKey.Number2, ModifierKeys.Control);
    public KeyCombination Stock2Buy1000 { get; set; } = new(VirtualKey.Number2, ModifierKeys.Alt | ModifierKeys.Shift);
    public KeyCombination Stock2SellAll { get; set; } = new(VirtualKey.Number2, ModifierKeys.Control | ModifierKeys.Shift);

    // Stock 3 Hotkeys
    public KeyCombination Stock3Buy100 { get; set; } = new(VirtualKey.Number3, ModifierKeys.Alt);
    public KeyCombination Stock3Sell100 { get; set; } = new(VirtualKey.Number3, ModifierKeys.Control);
    public KeyCombination Stock3Buy1000 { get; set; } = new(VirtualKey.Number3, ModifierKeys.Alt | ModifierKeys.Shift);
    public KeyCombination Stock3SellAll { get; set; } = new(VirtualKey.Number3, ModifierKeys.Control | ModifierKeys.Shift);

    // Trading Amounts
    public TradingAmounts DefaultAmounts { get; set; } = new();
}

public class KeyCombination
{
    public VirtualKey Key { get; set; }
    public ModifierKeys Modifiers { get; set; }

    public KeyCombination() { }

    public KeyCombination(VirtualKey key, ModifierKeys modifiers)
    {
        Key = key;
        Modifiers = modifiers;
    }

    public override string ToString()
    {
        var parts = new List<string>();

        if (Modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows))
            parts.Add("Win");

        parts.Add(Key.ToString());

        return string.Join(" + ", parts);
    }
}

public enum VirtualKey
{
    None = 0,
    Number0 = 48,
    Number1 = 49,
    Number2 = 50,
    Number3 = 51,
    Number4 = 52,
    Number5 = 53,
    Number6 = 54,
    Number7 = 55,
    Number8 = 56,
    Number9 = 57,
    A = 65,
    B = 66,
    C = 67,
    D = 68,
    E = 69,
    F = 70,
    G = 71,
    H = 72,
    I = 73,
    J = 74,
    K = 75,
    L = 76,
    M = 77,
    N = 78,
    O = 79,
    P = 80,
    Q = 81,
    R = 82,
    S = 83,
    T = 84,
    U = 85,
    V = 86,
    W = 87,
    X = 88,
    Y = 89,
    Z = 90,
    F1 = 112,
    F2 = 113,
    F3 = 114,
    F4 = 115,
    F5 = 116,
    F6 = 117,
    F7 = 118,
    F8 = 119,
    F9 = 120,
    F10 = 121,
    F11 = 122,
    F12 = 123,
    Enter = 13,
    Escape = 27,
    Space = 32,
    Delete = 46,
    Backspace = 8,
    Tab = 9
}

[Flags]
public enum ModifierKeys
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

public class TradingAmounts
{
    public bool UseDollarAmounts { get; set; } = false; // false = use share quantities

    // Share quantities
    public int SmallQuantity { get; set; } = 100;
    public int LargeQuantity { get; set; } = 1000;

    // Dollar amounts
    public decimal SmallDollarAmount { get; set; } = 1000m;
    public decimal LargeDollarAmount { get; set; } = 10000m;

    // Safety
    public bool RequireConfirmation { get; set; } = true;
    public bool EnableHotkeys { get; set; } = true;
}