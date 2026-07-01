using System.Security.Claims;

namespace Mirage.Api.Security;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new UnauthorizedAccessException("Authenticated user identifier is missing.");
    }

    public static Guid? TryGetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
