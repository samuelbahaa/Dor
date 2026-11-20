using System.Security.Claims;
using System.Text;
using Dor.Api.Dtos;
using Dor.Api.Models;
using Dor.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Dor.Api.Services;

// Creates the JWT the client sends back with every request ("Authorization: Bearer ...").
public class TokenService
{
    private readonly JwtOptions _jwt;

    public TokenService(IOptions<JwtOptions> jwt)
    {
        _jwt = jwt.Value;
    }

    public AuthResponse CreateAuthResponse(User user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_jwt.ExpiryMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Role, user.Role.ToString())
            }),
            Expires = expires,
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key)),
                SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new AuthResponse(token, expires, UserDto.From(user));
    }
}
