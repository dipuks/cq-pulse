using CQPulse.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Api.Errors;

/// <summary>
/// Translates expected application failures into RFC 7807 Problem Details. Anything not handled here falls
/// through to the default handler, which logs it and returns a generic 500 without internal details.
/// </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    private const int SqlUniqueIndexViolation = 2601;
    private const int SqlUniqueConstraintViolation = 2627;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            ValidationException validation => new ValidationProblemDetails(validation.Errors.ToDictionary(x => x.Key, x => x.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred."
            },
            NotFoundException notFound => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound, Title = "Resource not found.", Detail = notFound.Message
            },
            ConflictException conflict => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict, Title = "Request conflicts with the current state.", Detail = conflict.Message
            },
            DbUpdateException { InnerException: SqlException { Number: SqlUniqueIndexViolation or SqlUniqueConstraintViolation } } =>
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict, Title = "Request conflicts with the current state.",
                    Detail = "A record with the same unique value already exists."
                },
            _ => null
        };
        if (problem is null) return false;

        logger.LogInformation("Request {Method} {Path} rejected with {StatusCode}: {Reason}",
            httpContext.Request.Method, httpContext.Request.Path, problem.Status, problem.Detail ?? problem.Title);
        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
