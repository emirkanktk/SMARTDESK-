using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Resources;

namespace SmartDesk.API.Middleware;

// Single place that turns exceptions into RFC 7807 responses. Application-layer exceptions
// carry no HTTP knowledge; the status code mapping lives here. Titles are in the request's
// language, like the messages; a conflict with a code carries it as `code` (ADR-0023).
public class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = exception switch
        {
            ValidationException validation => new HttpValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = Messages.Problem_Validation,
            },
            UnauthorizedException => new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = Messages.Problem_Unauthorized,
                Detail = exception.Message,
            },
            ForbiddenAccessException => new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = Messages.Problem_Forbidden,
                Detail = exception.Message,
            },
            NotFoundException => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = Messages.Problem_NotFound,
                Detail = exception.Message,
            },
            ConflictException conflict => ConflictProblem(conflict),
            ServiceUnavailableException => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = Messages.Problem_ServiceUnavailable,
                Detail = exception.Message,
            },
            _ => null,
        };

        if (problemDetails is null)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

            problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = Messages.Problem_Unexpected,
            };
        }

        httpContext.Response.StatusCode = problemDetails.Status!.Value;

        // Writes application/problem+json and fills in the RFC type URI and traceId.
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception,
        });

        return true;
    }

    private static ProblemDetails ConflictProblem(ConflictException conflict)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = Messages.Problem_Conflict,
            Detail = conflict.Message,
        };
        if (conflict.Code is not null)
        {
            problem.Extensions["code"] = conflict.Code;
        }

        return problem;
    }
}
