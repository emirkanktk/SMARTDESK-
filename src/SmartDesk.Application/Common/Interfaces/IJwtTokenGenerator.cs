using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    AccessToken Generate(UserProfile user);
}
