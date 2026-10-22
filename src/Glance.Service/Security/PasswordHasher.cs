using System.Security.Cryptography;

namespace Glance.Service.Security;

/// <summary>PBKDF2-SHA256 password hashes stored as <c>pbkdf2-sha256$600000$&lt;salt b64&gt;$&lt;hash b64&gt;</c>.</summary>
public static class PasswordHasher
{
    private const string Scheme = "pbkdf2-sha256";
    private const int Iterations = 600_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>True when <paramref name="password"/> matches <paramref name="stored"/>; false (never throws) for malformed values.</summary>
    public static bool Verify(string password, string? stored)
    {
        var parts = stored?.Split('$');
        if (parts is not [Scheme, var iterations, var saltText, var hashText] || iterations != Iterations.ToString()) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(saltText);
            expected = Convert.FromBase64String(hashText);
        }
        catch (FormatException) { return false; }
        if (salt.Length != SaltBytes || expected.Length != HashBytes) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
