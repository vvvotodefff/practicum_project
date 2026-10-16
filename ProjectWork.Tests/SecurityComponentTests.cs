using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Application.Abstractions.Security;
using ProjectWork.Domain.Entities;
using ProjectWork.Infrastructure.Security;

namespace ProjectWork.Tests;

public class SecurityComponentTests
{
    private const string Secret = "Test-Only-Secret-At-Least-32-Bytes-Long";
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["Jwt:Secret"] = Secret,
        ["Jwt:Issuer"] = "test-issuer",
        ["Jwt:Audience"] = "test-audience",
        ["Jwt:LifetimeMinutes"] = "30"
    };

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static byte[] DecodePart(string part)
    {
        var base64 = part.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
    }

    [Fact]
    public void Hash_UsesSha256AndUppercaseHex()
    {
        var hasher = new Sha256PasswordHasher();
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
            hasher.Hash("abc"));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("пароль 🔒")]
    [InlineData(" password ")]
    public void Verify_MatchingPassword_Succeeds(string password)
    {
        var hasher = new Sha256PasswordHasher();
        Assert.True(hasher.Verify(password, hasher.Hash(password)));
        Assert.True(hasher.Verify(password, hasher.Hash(password).ToLowerInvariant()));
        Assert.False(hasher.Verify(password + "!", hasher.Hash(password)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public void Verify_InvalidOrDifferentHash_ReturnsFalse(string? hash)
    {
        Assert.False(new Sha256PasswordHasher().Verify("password", hash!));
    }

    [Fact]
    public void Password_IsNotTrimmedOrCaseNormalized()
    {
        var hasher = new Sha256PasswordHasher();
        Assert.False(hasher.Verify("Password", hasher.Hash("password")));
        Assert.False(hasher.Verify("password", hasher.Hash(" password ")));
        Assert.Throws<ArgumentNullException>(() => hasher.Hash(null!));
        Assert.False(hasher.Verify(null!, hasher.Hash("password")));
    }

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Admin)]
    public void Generate_ContainsClaimsAndValidHmacSignature(UserRole role)
    {
        var user = User.Create("alex", "private-password-hash", role);
        var generator = new JwtTokenGenerator(new JwtOptions(Secret, "issuer", "audience", 30), new FixedClock());
        var token = generator.Generate(user);
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);

        using var header = JsonDocument.Parse(DecodePart(parts[0]));
        Assert.Equal("HS256", header.RootElement.GetProperty("alg").GetString());
        using var payload = JsonDocument.Parse(DecodePart(parts[1]));
        var claims = payload.RootElement;
        Assert.Equal(user.Id.ToString(), claims.GetProperty("sub").GetString());
        Assert.Equal(user.Login, claims.GetProperty("name").GetString());
        Assert.Equal(role.ToString(), claims.GetProperty("role").GetString());
        Assert.Equal("issuer", claims.GetProperty("iss").GetString());
        Assert.Equal("audience", claims.GetProperty("aud").GetString());
        Assert.Equal(Now.ToUnixTimeSeconds(), claims.GetProperty("iat").GetInt64());
        Assert.Equal(Now.ToUnixTimeSeconds(), claims.GetProperty("nbf").GetInt64());
        Assert.Equal(Now.AddMinutes(30).ToUnixTimeSeconds(), claims.GetProperty("exp").GetInt64());
        Assert.True(Guid.TryParse(claims.GetProperty("jti").GetString(), out _));
        Assert.DoesNotContain("private-password-hash", claims.GetRawText());
        Assert.DoesNotContain(claims.EnumerateObject(), p =>
            p.Name.Contains("password", StringComparison.OrdinalIgnoreCase));

        // Проверяем подпись независимо от библиотеки, которая выдала токен.
        var input = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), input);
        Assert.Equal(expected, DecodePart(parts[2]));
        var wrongKeySignature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret + "wrong"), input);
        Assert.False(CryptographicOperations.FixedTimeEquals(wrongKeySignature, DecodePart(parts[2])));
        var changedInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1] + "x");
        Assert.False(CryptographicOperations.FixedTimeEquals(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), changedInput), DecodePart(parts[2])));
    }

    [Fact]
    public void Generate_UsesUniqueTokenIdAndRejectsNullUser()
    {
        var generator = new JwtTokenGenerator(new JwtOptions(Secret, "issuer", "audience", 30), new FixedClock());
        var user = User.Create("alex", "hash");
        Assert.NotEqual(generator.Generate(user), generator.Generate(user));
        Assert.Throws<ArgumentNullException>(() => generator.Generate(null!));
    }

    [Theory]
    [InlineData("Jwt:Secret", null)]
    [InlineData("Jwt:Secret", "short")]
    [InlineData("Jwt:Secret", "                                ")]
    [InlineData("Jwt:Issuer", "")]
    [InlineData("Jwt:Audience", null)]
    [InlineData("Jwt:LifetimeMinutes", null)]
    [InlineData("Jwt:LifetimeMinutes", "abc")]
    [InlineData("Jwt:LifetimeMinutes", "0")]
    [InlineData("Jwt:LifetimeMinutes", "-1")]
    public void Options_InvalidConfiguration_FailsWithoutLeakingSecret(string key, string? value)
    {
        var settings = Settings();
        settings[key] = value;
        var exception = Assert.Throws<InvalidOperationException>(() =>
            JwtOptions.FromConfiguration(Configuration(settings)));
        Assert.Contains(key, exception.Message);
        Assert.DoesNotContain(Secret, exception.Message);
    }

    [Fact]
    public void Registration_ResolvesComponentsAndPreservesCustomClock()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedClock());
        Assert.Same(services, services.AddSecurityServices(Configuration(Settings())));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true, ValidateOnBuild = true
        });
        var hasher = provider.GetRequiredService<IPasswordHasher>();
        Assert.IsType<Sha256PasswordHasher>(hasher);
        Assert.True(hasher.Verify("password", hasher.Hash("password")));
        var token = provider.GetRequiredService<IJwtTokenGenerator>().Generate(User.Create("alex", "hash"));
        using var payload = JsonDocument.Parse(DecodePart(token.Split('.')[1]));
        Assert.Equal("test-issuer", payload.RootElement.GetProperty("iss").GetString());
        Assert.Equal("test-audience", payload.RootElement.GetProperty("aud").GetString());
        Assert.Equal(Now.AddMinutes(30).ToUnixTimeSeconds(), payload.RootElement.GetProperty("exp").GetInt64());
    }
}
