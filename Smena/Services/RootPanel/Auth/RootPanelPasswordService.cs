using Host.Services.Data.Entities;
using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using System.Text;

namespace Host.Services.RootPanel;

internal interface IRootPanelPasswordService
{
    bool Verify(RootPanelUserEntity user, string rawPassword);
    string Hash(RootPanelUserEntity user, string rawPassword);
}

internal sealed class RootPanelPasswordService(
    IPasswordHasher<RootPanelUserEntity> passwordHasher) : IRootPanelPasswordService
{
    private readonly IPasswordHasher<RootPanelUserEntity> _passwordHasher = passwordHasher;

    public bool Verify(RootPanelUserEntity user, string rawPassword)
    {
        var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, rawPassword);
        if (verifyResult == PasswordVerificationResult.Success ||
            verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            return true;
        }

        // Backward compatibility for old SHA256 hashes.
        return string.Equals(HashPasswordLegacy(rawPassword), user.PasswordHash, StringComparison.Ordinal);
    }

    public string Hash(RootPanelUserEntity user, string rawPassword)
        => _passwordHasher.HashPassword(user, rawPassword);

    private static string HashPasswordLegacy(string rawPassword)
    {
        var bytes = Encoding.UTF8.GetBytes(rawPassword);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
