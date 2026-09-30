namespace SmartDesk.Application.Common.Exceptions;

// Something the request depends on outside the API - the assistant's language model - isn't
// set up or can't be reached right now. The message says which, in the request's language.
public sealed class ServiceUnavailableException(string message) : Exception(message);
