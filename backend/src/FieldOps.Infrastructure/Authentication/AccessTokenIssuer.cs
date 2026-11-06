using System.Security.Claims;
using System.Text;
using FieldOps.Application.Identity;
using FieldOps.Domain.Identity;
using FieldOps.Domain.Organizations;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FieldOps.Infrastructure.Authentication;

internal sealed class AccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock)
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(AuthSession session, OrganizationRole role)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(options.Value.AccessTokenMinutes);
        if (expires > session.ExpiresAt) expires = session.ExpiresAt;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity([
                new Claim("sub", session.UserId.ToString()),
                new Claim("sid", session.Id.ToString()),
                new Claim("tenant_id", session.TenantId.ToString()),
                new Claim("role", role.ToString()),
                new Claim("jti", Guid.NewGuid().ToString("N"))]),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
