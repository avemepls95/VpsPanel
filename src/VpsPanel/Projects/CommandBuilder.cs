using System.Text;
using VpsPanel.Overview;
using static VpsPanel.Ssh.ShellQuote;

namespace VpsPanel.Projects;

internal static class CommandBuilder
{
    /// <summary>
    /// Builds a command that changes to the folder and runs the script in bash -lc. If the folder change fails, the script is not run.
    /// </summary>
    public static string InFolder(string folder, string script) =>
        $"bash -lc {Q($"cd -- {Q(folder)} || exit 1\n{script}")} 2>&1";

    public static string ComposeUpdate(ComposeProjectView project)
    {
        var args = new StringBuilder($"-p {Q(project.Name)}");
        foreach (var file in project.ConfigFiles)
            args.Append($" -f {Q(file)}");
        if (!string.IsNullOrEmpty(project.EnvironmentFile))
            args.Append($" --env-file {Q(project.EnvironmentFile)}");
        return $"docker compose {args} pull && docker compose {args} up -d";
    }
}