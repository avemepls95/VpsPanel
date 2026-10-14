using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using VpsPanel.Files;
using VpsPanel.Runs;

namespace VpsPanel.Api;

internal static class FileEndpoints
{
    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        var files = app.MapGroup("/api/servers/{id}/files");
        files.MapGet("", (string id, string path, FileService service, CancellationToken ct) => service.List(id, path, ct));
        files.MapGet("/content", (string id, string path, FileService service, CancellationToken ct) => service.ReadContent(id, path, ct));
        files.MapPut("/content", (string id, SaveFileRequest request, FileService service, CancellationToken ct) => service.Save(id, request, ct));
        files.MapPost("/upload", Upload).DisableAntiforgery();
        files.MapGet("/download", async (string id, string path, HttpContext http, FileService service, CancellationToken ct) =>
        {
            await service.Download(id, path, http, ct);
            return Results.Empty;
        });
        files.MapDelete("", async (string id, string path, string? confirmName, FileService service, CancellationToken ct) =>
        {
            await service.Delete(id, path, confirmName, ct);
            return Results.NoContent();
        });

        app.MapPost("/api/files/copy", async (CopyRequest request, CopyService service, CancellationToken ct) =>
            new RunStarted((await service.Start(request, ct)).Id));
    }

    private static async Task<UploadResult> Upload(string id, string dir, bool? overwrite, HttpContext http, FileService service, CancellationToken ct)
    {
        var sizeLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
            sizeLimit.MaxRequestBodySize = null;

        var boundary = MediaTypeHeaderValue.TryParse(http.Request.ContentType, out var media)
                       && media.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            ? HeaderUtilities.RemoveQuotes(media.Boundary).Value
            : null;
        if (string.IsNullOrEmpty(boundary))
            throw new PanelException(400, "A multipart/form-data request was expected.");

        var reader = new MultipartReader(boundary, http.Request.Body);
        while (await reader.ReadNextSectionAsync(ct) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
                continue;
            var fileName = disposition.FileNameStar.HasValue
                ? disposition.FileNameStar.Value
                : HeaderUtilities.RemoveQuotes(disposition.FileName).Value;
            if (string.IsNullOrEmpty(fileName))
                continue;
            return await service.Upload(id, dir, fileName, overwrite ?? false, section.Body, ct);
        }
        throw new PanelException(400, "The request has no file.");
    }
}