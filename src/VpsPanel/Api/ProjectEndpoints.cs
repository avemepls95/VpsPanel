using VpsPanel.Projects;
using VpsPanel.Runs;

namespace VpsPanel.Api;

internal static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var server = app.MapGroup("/api/servers/{id}");

        server.MapPost("/compose/{project}/commands",
            (string id, string project, CommandRequest request, ProjectService projects, CancellationToken ct) =>
                projects.AddComposeCommand(id, project, request, ct));
        server.MapPut("/compose/{project}/commands/{commandId}",
            (string id, string project, string commandId, CommandRequest request, ProjectService projects) =>
                projects.UpdateComposeCommand(id, project, commandId, request));
        server.MapDelete("/compose/{project}/commands/{commandId}",
            (string id, string project, string commandId, ProjectService projects) =>
            {
                projects.DeleteComposeCommand(id, project, commandId);
                return Results.NoContent();
            });
        server.MapPost("/compose/{project}/update",
            (string id, string project, ProjectService projects) => new RunStarted(projects.UpdateCompose(id, project).Id));

        server.MapPost("/manual-projects",
            (string id, ManualProjectRequest request, ProjectService projects) => projects.AddManualProject(id, request));
        server.MapPut("/manual-projects/{projectId}",
            (string id, string projectId, ManualProjectRequest request, ProjectService projects) =>
                projects.UpdateManualProject(id, projectId, request));
        server.MapDelete("/manual-projects/{projectId}",
            (string id, string projectId, ProjectService projects) =>
            {
                projects.DeleteManualProject(id, projectId);
                return Results.NoContent();
            });
        server.MapPost("/manual-projects/{projectId}/commands",
            (string id, string projectId, CommandRequest request, ProjectService projects) =>
                projects.AddManualCommand(id, projectId, request));
        server.MapPut("/manual-projects/{projectId}/commands/{commandId}",
            (string id, string projectId, string commandId, CommandRequest request, ProjectService projects) =>
                projects.UpdateManualCommand(id, projectId, commandId, request));
        server.MapDelete("/manual-projects/{projectId}/commands/{commandId}",
            (string id, string projectId, string commandId, ProjectService projects) =>
            {
                projects.DeleteManualCommand(id, projectId, commandId);
                return Results.NoContent();
            });

        server.MapPost("/commands/{commandId}/run",
            (string id, string commandId, ProjectService projects) => new RunStarted(projects.RunCommand(id, commandId).Id));
    }
}