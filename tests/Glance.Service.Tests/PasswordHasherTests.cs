using Glance.Service.Security;

namespace Glance.Service.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_roundtrip()
    {
        var h = PasswordHasher.Hash("correct horse");
        Assert.True(PasswordHasher.Verify("correct horse", h));
        Assert.False(PasswordHasher.Verify("nope", h));
        Assert.StartsWith("pbkdf2-sha256$600000$", h);
    }

    [Fact]
    public void Hash_uses_16_byte_salt_and_32_byte_hash_and_a_fresh_salt_each_time()
    {
        var parts = PasswordHasher.Hash("correct horse").Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
        Assert.NotEqual(PasswordHasher.Hash("correct horse"), PasswordHasher.Hash("correct horse"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("pbkdf2-sha256$600000$")]
    [InlineData("pbkdf2-sha256$600000$!!!$!!!")]
    [InlineData("pbkdf2-sha256$abc$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$600000$AAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("md5$600000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void Verify_returns_false_for_malformed_stored_values(string? stored) =>
        Assert.False(PasswordHasher.Verify("correct horse", stored));
}
