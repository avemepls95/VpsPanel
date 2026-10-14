using Microsoft.AspNetCore.Mvc;

namespace VpsPanel.Security;

internal sealed class CsrfHeaderMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-VpsPanel";

    public Task Invoke(HttpContext context)
    {
        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)
            || context.Request.Headers[HeaderName] == "1")
            return next(context);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Request rejected",
            Detail = $"Header {HeaderName} is missing."
        });
    }
}