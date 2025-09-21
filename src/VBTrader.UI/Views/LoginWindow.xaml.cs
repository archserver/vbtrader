using Microsoft.UI.Xaml;
using VBTrader.UI.ViewModels;

namespace VBTrader.UI.Views;

public sealed partial class LoginWindow : Window
{
    public LoginViewModel ViewModel { get; }

    public LoginWindow(LoginViewModel viewModel)
    {
        ViewModel = viewModel;
        this.InitializeComponent();

        // Set window properties
        Title = "VBTrader - Login";
        ExtendsContentIntoTitleBar = true;

        // Center the window
        var displayArea = Microsoft.UI.Windowing.DisplayArea.Primary;
        var centerX = (displayArea.WorkArea.Width - 500) / 2;
        var centerY = (displayArea.WorkArea.Height - 600) / 2;

        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(centerX, centerY, 500, 600));
    }
}