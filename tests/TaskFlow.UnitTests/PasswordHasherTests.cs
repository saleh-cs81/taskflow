using TaskFlow.Infrastructure.Services;

namespace TaskFlow.UnitTests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_then_Verify_succeeds_for_correct_password()
    {
        var hash = _hasher.Hash("Sup3rSecret!");
        Assert.True(_hasher.Verify("Sup3rSecret!", hash));
    }

    [Fact]
    public void Verify_fails_for_wrong_password()
    {
        var hash = _hasher.Hash("Sup3rSecret!");
        Assert.False(_hasher.Verify("wrong", hash));
    }

    [Fact]
    public void Hash_is_salted_so_two_hashes_differ()
    {
        Assert.NotEqual(_hasher.Hash("same"), _hasher.Hash("same"));
    }

    [Fact]
    public void Verify_returns_false_for_malformed_hash()
    {
        Assert.False(_hasher.Verify("x", "not-a-valid-hash"));
    }
}
