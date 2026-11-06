using System.Diagnostics;
using FieldOps.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Errors;

public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var failure = exception as FieldOpsException;
        var status = failure?.Kind switch
        {
            ErrorKind.Validation => 400,
            ErrorKind.Unauthenticated => 401,
            ErrorKind.Forbidden => 403,
            ErrorKind.NotFound => 404,
            ErrorKind.Conflict => 409,
            ErrorKind.Unavailable => 503,
            _ => exception is BadHttpRequestException badRequest ? badRequest.StatusCode : 500
        };
        // Public application errors are deliberately safe. Never log raw provider/request exceptions.
        if (status >= 500 && failure is null)
            LogUnhandledError(logger, exception.GetType().Name, httpContext.TraceIdentifier);
        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = failure?.Message ?? (status < 500 ? "The request is invalid." : "An unexpected error occurred."),
            Extensions = { ["traceId"] = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier }
        };
        problem.Extensions["code"] = failure?.Code ?? (status < 500 ? "invalid_request" : "unexpected_error");

        if (!await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem
        }))
        {
            // Unsupported Accept headers must not bypass safe handling and expose raw exception diagnostics.
            await httpContext.Response.WriteAsJsonAsync(problem, options: null,
                contentType: "application/problem+json", cancellationToken);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled {ExceptionType}; request {RequestId}")]
    private static partial void LogUnhandledError(ILogger logger, string exceptionType, string requestId);
}
