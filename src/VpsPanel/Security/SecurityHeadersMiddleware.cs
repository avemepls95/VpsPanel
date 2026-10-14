namespace VpsPanel.Security;

internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task Invoke(HttpContext context)
    {
        // OnStarting survives the response being cleared by the exception handler.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.ContentSecurityPolicy = "frame-ancestors 'none'; script-src 'self'; object-src 'none'";
            context.Response.Headers.XFrameOptions = "DENY";
            return Task.CompletedTask;
        });
        return next(context);
    }
}