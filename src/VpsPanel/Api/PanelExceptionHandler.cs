using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace VpsPanel.Api;

internal sealed class PanelExceptionHandler(IProblemDetailsService problems, ILogger<PanelExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception is PanelException panel ? panel.StatusCode : StatusCodes.Status500InternalServerError;
        if (status == StatusCodes.Status500InternalServerError && exception is not PanelException)
            logger.LogError(exception, "Unhandled error");

        httpContext.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = "Error",
                Detail = exception.Message
            }
        });
    }
}