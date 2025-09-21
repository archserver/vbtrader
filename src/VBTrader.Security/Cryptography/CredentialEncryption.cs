using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VBTrader.Security.Cryptography;

public class CredentialEncryption : ICredentialEncryption
{
    private const int KeySize = 32; // 256 bits
    private const int IvSize = 16;  // 128 bits
    private const int SaltSize = 16;
    private const int Iterations = 10000;

    public async Task<string> EncryptCredentialsAsync(SchwabCredentials credentials, string password)
    {
        var json = JsonSerializer.Serialize(credentials);
        var plainTextBytes = Encoding.UTF8.GetBytes(json);

        var salt = GenerateSalt();
        var key = DeriveKey(password, salt);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        using var memoryStream = new MemoryStream();
        using var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write);

        await cryptoStream.WriteAsync(plainTextBytes);
        await cryptoStream.FlushFinalBlockAsync();

        var encryptedBytes = memoryStream.ToArray();

        // Combine salt + IV + encrypted data
        var result = new byte[SaltSize + IvSize + encryptedBytes.Length];
        Array.Copy(salt, 0, result, 0, SaltSize);
        Array.Copy(aes.IV, 0, result, SaltSize, IvSize);
        Array.Copy(encryptedBytes, 0, result, SaltSize + IvSize, encryptedBytes.Length);

        return Convert.ToBase64String(result);
    }

    public async Task<SchwabCredentials?> DecryptCredentialsAsync(string encryptedData, string password)
    {
        try
        {
            var dataBytes = Convert.FromBase64String(encryptedData);

            if (dataBytes.Length < SaltSize + IvSize)
                return null;

            var salt = new byte[SaltSize];
            var iv = new byte[IvSize];
            var encryptedBytes = new byte[dataBytes.Length - SaltSize - IvSize];

            Array.Copy(dataBytes, 0, salt, 0, SaltSize);
            Array.Copy(dataBytes, SaltSize, iv, 0, IvSize);
            Array.Copy(dataBytes, SaltSize + IvSize, encryptedBytes, 0, encryptedBytes.Length);

            var key = DeriveKey(password, salt);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var memoryStream = new MemoryStream(encryptedBytes);
            using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
            using var reader = new StreamReader(cryptoStream);

            var json = await reader.ReadToEndAsync();
            return JsonSerializer.Deserialize<SchwabCredentials>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveEncryptedCredentialsAsync(SchwabCredentials credentials, string password, string filePath)
    {
        var encryptedData = await EncryptCredentialsAsync(credentials, password);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(filePath, encryptedData);
    }

    public async Task<SchwabCredentials?> LoadEncryptedCredentialsAsync(string password, string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var encryptedData = await File.ReadAllTextAsync(filePath);
        return await DecryptCredentialsAsync(encryptedData, password);
    }

    private byte[] GenerateSalt()
    {
        using var rng = RandomNumberGenerator.Create();
        var salt = new byte[SaltSize];
        rng.GetBytes(salt);
        return salt;
    }

    private byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }
}

public interface ICredentialEncryption
{
    Task<string> EncryptCredentialsAsync(SchwabCredentials credentials, string password);
    Task<SchwabCredentials?> DecryptCredentialsAsync(string encryptedData, string password);
    Task SaveEncryptedCredentialsAsync(SchwabCredentials credentials, string password, string filePath);
    Task<SchwabCredentials?> LoadEncryptedCredentialsAsync(string password, string filePath);
}

public class SchwabCredentials
{
    public string AppKey { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = "https://127.0.0.1";
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiry { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}