using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Api.Endpoints;

internal static class EndpointUserContext
{
    public static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var userIdRaw = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(userIdRaw, out userId);
    }
}
