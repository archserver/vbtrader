using System;
using VBTrader.Security.Cryptography;

class Program
{
    static void Main()
    {
        var hasher = new PasswordHasher();
        string password = "password123";

        Console.WriteLine("=== HASH CREATION TEST ===");
        Console.WriteLine($"Input password: '{password}'");

        // Create hash
        var hash1 = hasher.HashPassword(password);
        Console.WriteLine($"Generated hash 1: {hash1}");
        Console.WriteLine($"Hash 1 length: {hash1.Length}");

        // Create another hash with same password
        var hash2 = hasher.HashPassword(password);
        Console.WriteLine($"Generated hash 2: {hash2}");
        Console.WriteLine($"Hash 2 length: {hash2.Length}");

        Console.WriteLine($"Hashes are different (expected): {hash1 != hash2}");

        Console.WriteLine("\n=== VERIFICATION TEST ===");

        // Test verification with first hash
        bool verify1 = hasher.VerifyPassword(password, hash1);
        Console.WriteLine($"Verify password against hash 1: {verify1}");

        // Test verification with second hash
        bool verify2 = hasher.VerifyPassword(password, hash2);
        Console.WriteLine($"Verify password against hash 2: {verify2}");

        // Test with wrong password
        bool verify3 = hasher.VerifyPassword("wrongpassword", hash1);
        Console.WriteLine($"Verify wrong password against hash 1: {verify3}");

        Console.WriteLine("\n=== EXPECTED RESULTS ===");
        Console.WriteLine("verify1 should be: True");
        Console.WriteLine("verify2 should be: True");
        Console.WriteLine("verify3 should be: False");
    }
}