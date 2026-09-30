using MediatR;
using SmartDesk.Application.Features.Auth.Queries.GetCurrentUser;

namespace SmartDesk.Application.Features.Auth.Commands.UpdateProfile;

// The signed-in user's own name (PUT /api/auth/me). The email and the role are changed by an
// administrator (ADR-0020).
public sealed record UpdateProfileCommand(string FirstName, string LastName) : IRequest<CurrentUserDto>;
