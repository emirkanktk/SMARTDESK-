namespace SmartDesk.Application.Common.Exceptions;

// Mapped to 404 by the API.
public sealed class NotFoundException(string entityName, object key)
    : Exception($"{entityName} '{key}' was not found.");
