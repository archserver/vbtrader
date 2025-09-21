using Microsoft.UI.Xaml.Controls;

namespace VBTrader.UI.Services;

public interface INavigationService
{
    void NavigateTo<T>() where T : Page;
    void NavigateTo<T>(object parameter) where T : Page;
    void NavigateToMainWindow();
    void NavigateToLogin();
    void NavigateToSettings();
    bool CanGoBack { get; }
    void GoBack();
    void SetFrame(Frame frame);
}

public class NavigationService : INavigationService
{
    private Frame? _frame;

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public void SetFrame(Frame frame)
    {
        _frame = frame;
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
        {
            _frame.GoBack();
        }
    }

    public void NavigateTo<T>() where T : Page
    {
        _frame?.Navigate(typeof(T));
    }

    public void NavigateTo<T>(object parameter) where T : Page
    {
        _frame?.Navigate(typeof(T), parameter);
    }

    public void NavigateToMainWindow()
    {
        var mainWindow = App.GetService<MainWindow>();
        App.Current.MainWindow = mainWindow;
        mainWindow.Activate();
    }

    public void NavigateToLogin()
    {
        var loginWindow = App.GetService<LoginWindow>();
        App.Current.MainWindow = loginWindow;
        loginWindow.Activate();
    }

    public void NavigateToSettings()
    {
        var settingsWindow = App.GetService<SettingsWindow>();
        settingsWindow.Activate();
    }
}