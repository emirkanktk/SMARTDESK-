using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Common.Exceptions;

// The caller's identity couldn't be established: wrong credentials, or a token for a user
// that no longer exists. Mapped to 401 by the API.
public sealed class UnauthorizedException : Exception
{
    public UnauthorizedException()
        : base(Messages.Error_Unauthorized)
    {
    }

    public UnauthorizedException(string message)
        : base(message)
    {
    }
}
