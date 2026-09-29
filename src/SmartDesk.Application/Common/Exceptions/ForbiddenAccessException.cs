using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Common.Exceptions;

// The caller is authenticated but not allowed to act on this particular item (e.g. cancelling
// someone else's reservation). Role-level checks stay on the endpoint ([Authorize(Roles)]);
// this is for rules that depend on the data. Mapped to 403 by the API.
public sealed class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException()
        : base(Messages.Error_Forbidden)
    {
    }

    public ForbiddenAccessException(string message)
        : base(message)
    {
    }
}
