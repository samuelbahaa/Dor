using System.Security.Claims;

namespace Dor.Api.Services;

public static class UserClaimsExtensions
{
    // Reads the logged-in user's id from the JWT.
    public static int GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, out var id) ? id : 0;
    }
}
