using System.Security.Cryptography;
using System.Text;
using ProjectWork.Users.Application.Abstractions.Security;

namespace ProjectWork.Users.Infrastructure.Security;

/// <summary>Учебная реализация SHA-256 согласно заданию спринта.</summary>
public sealed class Sha256PasswordHasher : IPasswordHasher
{
    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
    }

    /// <inheritdoc />
    public bool Verify(string password, string passwordHash)
    {
        if (password is null || passwordHash is null || passwordHash.Length != 64)
            return false;

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(passwordHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
