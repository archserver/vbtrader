using Microsoft.UI.Xaml;

namespace VBTrader.UI.Services;

public interface IThemeService
{
    ElementTheme CurrentTheme { get; }
    void SetTheme(ElementTheme theme);
    void ToggleTheme();
    event EventHandler<ElementTheme>? ThemeChanged;
}

public class ThemeService : IThemeService
{
    private ElementTheme _currentTheme = ElementTheme.Dark;

    public ElementTheme CurrentTheme => _currentTheme;

    public event EventHandler<ElementTheme>? ThemeChanged;

    public void SetTheme(ElementTheme theme)
    {
        if (_currentTheme == theme) return;

        _currentTheme = theme;

        // Apply theme to current window
        if (App.Current.MainWindow?.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = theme;
        }

        ThemeChanged?.Invoke(this, theme);
    }

    public void ToggleTheme()
    {
        var newTheme = _currentTheme == ElementTheme.Dark
            ? ElementTheme.Light
            : ElementTheme.Dark;

        SetTheme(newTheme);
    }
}