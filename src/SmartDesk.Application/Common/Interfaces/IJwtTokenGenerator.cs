using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    // Under the sign-in session it belongs to (ADR-0026).
    AccessToken Generate(UserProfile user, Guid sessionId);
}
