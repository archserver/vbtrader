using VBTrader.Security.Cryptography;

namespace VBTrader.Security.Authentication;

public class UserAuthenticationService : IUserAuthenticationService
{
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICredentialEncryption _credentialEncryption;
    private readonly string _appDataPath;
    private readonly string _userConfigPath;

    private bool _isAuthenticated = false;
    private string? _currentPassword;

    public UserAuthenticationService(IPasswordHasher passwordHasher, ICredentialEncryption credentialEncryption)
    {
        _passwordHasher = passwordHasher;
        _credentialEncryption = credentialEncryption;

        _appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VBTrader");
        _userConfigPath = Path.Combine(_appDataPath, "user.config");

        EnsureDirectoryExists();
    }

    public bool IsAuthenticated => _isAuthenticated;

    public async Task<bool> LoginAsync(string password)
    {
        try
        {
            if (!File.Exists(_userConfigPath))
                return false;

            var userConfig = await LoadUserConfigAsync();
            if (userConfig == null)
                return false;

            var isValidPassword = _passwordHasher.VerifyPassword(password, userConfig.PasswordHash);
            if (isValidPassword)
            {
                _isAuthenticated = true;
                _currentPassword = password;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RegisterAsync(string password)
    {
        try
        {
            if (File.Exists(_userConfigPath))
                return false; // User already exists

            var passwordHash = _passwordHasher.HashPassword(password);
            var userConfig = new UserConfig
            {
                PasswordHash = passwordHash,
                CreatedAt = DateTime.UtcNow
            };

            await SaveUserConfigAsync(userConfig);

            _isAuthenticated = true;
            _currentPassword = password;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        if (!_isAuthenticated || _currentPassword != currentPassword)
            return false;

        try
        {
            var userConfig = await LoadUserConfigAsync();
            if (userConfig == null)
                return false;

            // Verify current password
            if (!_passwordHasher.VerifyPassword(currentPassword, userConfig.PasswordHash))
                return false;

            // Re-encrypt credentials with new password if they exist
            var credentialsPath = GetCredentialsPath();
            SchwabCredentials? credentials = null;

            if (File.Exists(credentialsPath))
            {
                credentials = await _credentialEncryption.LoadEncryptedCredentialsAsync(currentPassword, credentialsPath);
                if (credentials == null)
                    return false; // Couldn't decrypt existing credentials
            }

            // Update password hash
            userConfig.PasswordHash = _passwordHasher.HashPassword(newPassword);
            userConfig.LastUpdated = DateTime.UtcNow;
            await SaveUserConfigAsync(userConfig);

            // Re-encrypt credentials with new password
            if (credentials != null)
            {
                await _credentialEncryption.SaveEncryptedCredentialsAsync(credentials, newPassword, credentialsPath);
            }

            _currentPassword = newPassword;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Logout()
    {
        _isAuthenticated = false;
        _currentPassword = null;
    }

    public async Task<bool> SaveSchwabCredentialsAsync(SchwabCredentials credentials)
    {
        if (!_isAuthenticated || string.IsNullOrEmpty(_currentPassword))
            return false;

        try
        {
            var credentialsPath = GetCredentialsPath();
            await _credentialEncryption.SaveEncryptedCredentialsAsync(credentials, _currentPassword, credentialsPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<SchwabCredentials?> LoadSchwabCredentialsAsync()
    {
        if (!_isAuthenticated || string.IsNullOrEmpty(_currentPassword))
            return null;

        try
        {
            var credentialsPath = GetCredentialsPath();
            return await _credentialEncryption.LoadEncryptedCredentialsAsync(_currentPassword, credentialsPath);
        }
        catch
        {
            return null;
        }
    }

    public bool HasExistingUser()
    {
        return File.Exists(_userConfigPath);
    }

    private async Task<UserConfig?> LoadUserConfigAsync()
    {
        try
        {
            var json = await File.ReadAllTextAsync(_userConfigPath);
            return System.Text.Json.JsonSerializer.Deserialize<UserConfig>(json);
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveUserConfigAsync(UserConfig userConfig)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(userConfig, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(_userConfigPath, json);
    }

    private string GetCredentialsPath()
    {
        return Path.Combine(_appDataPath, "schwab.credentials");
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_appDataPath))
        {
            Directory.CreateDirectory(_appDataPath);
        }
    }
}

public interface IUserAuthenticationService
{
    bool IsAuthenticated { get; }
    Task<bool> LoginAsync(string password);
    Task<bool> RegisterAsync(string password);
    Task<bool> ChangePasswordAsync(string currentPassword, string newPassword);
    void Logout();
    Task<bool> SaveSchwabCredentialsAsync(SchwabCredentials credentials);
    Task<SchwabCredentials?> LoadSchwabCredentialsAsync();
    bool HasExistingUser();
}

public class UserConfig
{
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}