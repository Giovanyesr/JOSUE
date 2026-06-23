using System.Security.Cryptography;

namespace AsistenciaColegio.Services;

public sealed class PasswordService
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    public string Hash(string value)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(value, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"PBKDF2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public bool Verify(string candidate, string hash)
    {
        if (!hash.StartsWith("PBKDF2$", StringComparison.OrdinalIgnoreCase))
        {
            var legacy = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(candidate));
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), legacy);
        }

        var parts = hash.Split('$');
        if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(candidate, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(string hash) => !hash.StartsWith("PBKDF2$", StringComparison.OrdinalIgnoreCase);
}
