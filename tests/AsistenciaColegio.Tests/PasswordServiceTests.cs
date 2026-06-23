using System.Security.Cryptography;
using System.Text;
using AsistenciaColegio.Services;

namespace AsistenciaColegio.Tests;

public sealed class PasswordServiceTests
{
    private readonly PasswordService _service = new();

    [Fact]
    public void Hash_returns_non_empty_string()
    {
        var hash = _service.Hash("mypassword");
        Assert.False(string.IsNullOrWhiteSpace(hash));
    }

    [Fact]
    public void Verify_correct_password_returns_true()
    {
        var hash = _service.Hash("correct-password");
        Assert.True(_service.Verify("correct-password", hash));
    }

    [Fact]
    public void Verify_wrong_password_returns_false()
    {
        var hash = _service.Hash("real-password");
        Assert.False(_service.Verify("wrong-password", hash));
    }

    [Fact]
    public void Verify_legacy_sha256_hash_works()
    {
        var sha256Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("legacy-pass")));
        Assert.True(_service.Verify("legacy-pass", sha256Hash));
    }

    [Fact]
    public void Verify_legacy_sha256_rejects_wrong_password()
    {
        var sha256Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("legacy-pass")));
        Assert.False(_service.Verify("wrong-legacy-pass", sha256Hash));
    }

    [Fact]
    public void NeedsRehash_returns_true_for_sha256_hash()
    {
        var sha256Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("test")));
        Assert.True(_service.NeedsRehash(sha256Hash));
    }

    [Fact]
    public void NeedsRehash_returns_false_for_pbkdf2_hash()
    {
        var hash = _service.Hash("test");
        Assert.False(_service.NeedsRehash(hash));
    }

    [Fact]
    public void Verify_tampered_hash_returns_false()
    {
        var hash = _service.Hash("mypassword");
        var chars = hash.ToCharArray();
        chars[^1] = chars[^1] == 'A' ? 'B' : 'A';
        var tampered = new string(chars);
        Assert.False(_service.Verify("mypassword", tampered));
    }
}
