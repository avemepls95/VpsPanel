using VpsPanel.Files;

namespace VpsPanel.Projects;

internal sealed record CommandRequest(string? Name, string? Script)
{
    public (string Name, string Script) Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new PanelException(400, "Enter the command name.");
        if (string.IsNullOrWhiteSpace(Script))
            throw new PanelException(400, "Enter the command script.");
        return (Name.Trim(), Script.Replace("\r\n", "\n"));
    }
}

internal sealed record ManualProjectRequest(string? Name, string? Folder)
{
    public (string Name, string Folder) Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new PanelException(400, "Enter the project name.");
        if (string.IsNullOrWhiteSpace(Folder))
            throw new PanelException(400, "Enter the project folder.");
        return (Name.Trim(), RemotePath.Normalize(Folder.Trim()));
    }
}