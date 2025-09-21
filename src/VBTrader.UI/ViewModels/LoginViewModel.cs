using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VBTrader.Security.Authentication;
using VBTrader.Security.Cryptography;
using VBTrader.UI.Services;

namespace VBTrader.UI.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IUserAuthenticationService _authService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private bool _isLoginMode = true;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasExistingUser;

    public LoginViewModel(
        IUserAuthenticationService authService,
        INavigationService navigationService,
        IDialogService dialogService)
    {
        _authService = authService;
        _navigationService = navigationService;
        _dialogService = dialogService;

        _hasExistingUser = _authService.HasExistingUser();
        _isLoginMode = _hasExistingUser;

        StatusMessage = _hasExistingUser
            ? "Enter your password to access VBTrader"
            : "Create a secure password to protect your trading data";
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrEmpty(Password))
        {
            StatusMessage = "Please enter your password";
            return;
        }

        IsLoading = true;
        StatusMessage = "Authenticating...";

        try
        {
            bool success;

            if (IsLoginMode)
            {
                success = await _authService.LoginAsync(Password);
                if (!success)
                {
                    StatusMessage = "Invalid password. Please try again.";
                    Password = string.Empty;
                    return;
                }
            }
            else
            {
                if (Password != ConfirmPassword)
                {
                    StatusMessage = "Passwords do not match";
                    return;
                }

                if (Password.Length < 8)
                {
                    StatusMessage = "Password must be at least 8 characters long";
                    return;
                }

                success = await _authService.RegisterAsync(Password);
                if (!success)
                {
                    StatusMessage = "Failed to create account. Please try again.";
                    return;
                }
            }

            if (success)
            {
                StatusMessage = "Authentication successful";
                await Task.Delay(500); // Brief delay to show success message
                _navigationService.NavigateToMainWindow();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            await _dialogService.ShowErrorAsync("Authentication Error", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ToggleMode()
    {
        IsLoginMode = !IsLoginMode;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        StatusMessage = IsLoginMode
            ? "Enter your password to access VBTrader"
            : "Create a secure password to protect your trading data";
    }

    [RelayCommand]
    private async Task ForgotPasswordAsync()
    {
        await _dialogService.ShowMessageAsync(
            "Password Recovery",
            "Password recovery is not available. If you've forgotten your password, " +
            "you'll need to delete the application data and start fresh. " +
            "\n\nThis will permanently delete all saved credentials and settings.");
    }

    [RelayCommand]
    private async Task ShowHelpAsync()
    {
        await _dialogService.ShowMessageAsync(
            "VBTrader Security",
            "VBTrader uses military-grade Argon2 encryption to protect your trading credentials. " +
            "\n\n• Your password is never stored - only a secure hash" +
            "\n• All Schwab API credentials are encrypted with AES-256" +
            "\n• Data is stored locally and never transmitted to external servers" +
            "\n\nChoose a strong password that you'll remember - recovery is not possible.");
    }
}