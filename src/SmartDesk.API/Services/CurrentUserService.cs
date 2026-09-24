using System.Security.Claims;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.API.Services;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    // The JWT bearer handler maps the token's "sub" claim to ClaimTypes.NameIdentifier.
    public Guid? UserId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public bool IsInRole(string role) => httpContextAccessor.HttpContext?.User.IsInRole(role) ?? false;
}
