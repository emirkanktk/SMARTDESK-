using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Common.Exceptions;

// Mapped to 404 by the API. The entity name is translated when Messages knows it
// ("Entity_Resource"), so the message reads naturally in either language.
public sealed class NotFoundException(string entityName, object key)
    : Exception(string.Format(Messages.Error_NotFound, Describe(entityName), key))
{
    private static string Describe(string entityName) =>
        Messages.ResourceManager.GetString($"Entity_{entityName}") ?? entityName;
}
