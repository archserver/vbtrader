using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VBTrader.UI.ViewModels;
using VBTrader.Core.Models;

namespace VBTrader.UI.Views;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; private set; }

    public MainWindow()
    {
        this.InitializeComponent();
        ViewModel = new MainViewModel();
        this.DataContext = ViewModel;

        // Setup hotkeys
        SetupHotkeys();

        // Start real-time data collection
        _ = ViewModel.StartRealTimeDataAsync();
    }

    private void SetupHotkeys()
    {
        // Register global hotkeys for trading
        this.KeyDown += MainWindow_KeyDown;
    }

    private async void MainWindow_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var isAltPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (isAltPressed)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Number1:
                    await ExecuteBuyOrder(1);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Number2:
                    await ExecuteBuyOrder(2);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Number3:
                    await ExecuteBuyOrder(3);
                    e.Handled = true;
                    break;
            }
        }
        else if (isCtrlPressed)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Number1:
                    await ExecuteSellOrder(1);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Number2:
                    await ExecuteSellOrder(2);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Number3:
                    await ExecuteSellOrder(3);
                    e.Handled = true;
                    break;
            }
        }
    }

    private async Task ExecuteBuyOrder(int stockNumber)
    {
        var symbol = stockNumber switch
        {
            1 => Symbol1TextBox.Text?.Trim().ToUpper(),
            2 => Symbol2TextBox.Text?.Trim().ToUpper(),
            3 => Symbol3TextBox.Text?.Trim().ToUpper(),
            _ => null
        };

        if (!string.IsNullOrEmpty(symbol))
        {
            await ViewModel.ExecuteBuyOrderAsync(symbol, 100);
        }
    }

    private async Task ExecuteSellOrder(int stockNumber)
    {
        var symbol = stockNumber switch
        {
            1 => Symbol1TextBox.Text?.Trim().ToUpper(),
            2 => Symbol2TextBox.Text?.Trim().ToUpper(),
            3 => Symbol3TextBox.Text?.Trim().ToUpper(),
            _ => null
        };

        if (!string.IsNullOrEmpty(symbol))
        {
            await ViewModel.ExecuteSellOrderAsync(symbol, 100);
        }
    }

    // Button Click Handlers
    private async void BuyStock1_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteBuyOrder(1);
    }

    private async void BuyStock2_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteBuyOrder(2);
    }

    private async void BuyStock3_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteBuyOrder(3);
    }

    private async void SellStock1_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteSellOrder(1);
    }

    private async void SellStock2_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteSellOrder(2);
    }

    private async void SellStock3_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteSellOrder(3);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshDataAsync();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Open settings window
        ViewModel.AddTradeStatus("Settings window not implemented yet");
    }
}