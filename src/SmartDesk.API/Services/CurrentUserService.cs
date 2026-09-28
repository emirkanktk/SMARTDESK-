using System.Security.Claims;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Security;

namespace SmartDesk.API.Services;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    // The JWT bearer handler maps the token's "sub" claim to ClaimTypes.NameIdentifier.
    public Guid? UserId =>
        Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    // Added while the token was validated, from the account as it is now (JwtBearerSetup).
    public Guid? TeamId =>
        Guid.TryParse(User?.FindFirstValue(SmartDeskClaims.Team), out var team) ? team : null;

    public bool IsInRole(string role) => User?.IsInRole(role) ?? false;
}
