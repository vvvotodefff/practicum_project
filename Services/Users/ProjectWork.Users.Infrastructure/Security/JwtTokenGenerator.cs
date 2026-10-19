using JWT.Algorithms;
using JWT.Builder;
using ProjectWork.Users.Application.Abstractions.Security;
using ProjectWork.Users.Domain.Entities;

namespace ProjectWork.Users.Infrastructure.Security;

/// <summary>Подписывает JWT алгоритмом HS256; не включает хеш пароля в токен.</summary>
public sealed class JwtTokenGenerator(JwtOptions options, TimeProvider timeProvider) : IJwtTokenGenerator
{
    /// <inheritdoc />
    public string Generate(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = timeProvider.GetUtcNow();

        return JwtBuilder.Create()
            .WithAlgorithm(new HMACSHA256Algorithm())
            .WithSecret(options.Secret)
            .AddClaim("sub", user.Id.ToString())
            .AddClaim("name", user.Login)
            .AddClaim("role", user.Role.ToString())
            .AddClaim("iss", options.Issuer)
            .AddClaim("aud", options.Audience)
            .AddClaim("iat", now.ToUnixTimeSeconds())
            .AddClaim("nbf", now.ToUnixTimeSeconds())
            .AddClaim("exp", now.AddMinutes(options.LifetimeMinutes).ToUnixTimeSeconds())
            .AddClaim("jti", Guid.NewGuid().ToString())
            .Encode();
    }
}
