namespace SmartDesk.Application.Common.Exceptions;

// The caller's identity couldn't be established: wrong credentials, or a token for a user
// that no longer exists. Mapped to 401 by the API.
public sealed class UnauthorizedException(string message = "Authentication is required.")
    : Exception(message);
