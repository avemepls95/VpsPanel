using VpsPanel.Ssh;

namespace VpsPanel.Overview;

internal static class OverviewScript
{
    // The template prints each container as a separate JSON object on one line. Compose labels are taken by name,
    // because the standard Labels field joins labels with commas, and config_files already contains commas.
    public const string DockerFormat =
        """{"name":{{json .Names}},"image":{{json .Image}},"state":{{json .State}},"status":{{json .Status}},"ports":{{json .Ports}},"project":{{json (.Label "com.docker.compose.project")}},"workingDir":{{json (.Label "com.docker.compose.project.working_dir")}},"configFiles":{{json (.Label "com.docker.compose.project.config_files")}},"environmentFile":{{json (.Label "com.docker.compose.project.environment_file")}}}""";

    private const string Script = """
        echo '@@hostname'
        hostname
        echo '@@os'
        ( . /etc/os-release 2>/dev/null && echo "$PRETTY_NAME" )
        echo '@@uptime'
        cut -d' ' -f1 /proc/uptime
        echo '@@cpus'
        nproc
        echo '@@mem'
        free -b | awk '/^Mem:/ {print $2, $3}'
        echo '@@disk'
        df -B1 --output=size,used / | tail -n 1
        echo '@@docker'
        if command -v docker >/dev/null 2>&1; then
          echo available
          docker ps -a --format <<DOCKER_FORMAT>>
        fi
        echo '@@timers'
        systemctl list-timers --all --output=json --no-pager 2>/dev/null
        echo '@@timer-files'
        for f in /etc/systemd/system/*.timer; do [ -e "$f" ] && basename "$f"; done
        echo '@@results'
        for f in /etc/systemd/system/*.timer; do
          [ -e "$f" ] || continue
          unit=$(systemctl show -p Unit --value "$(basename "$f")" 2>/dev/null)
          [ -n "$unit" ] && systemctl show -p Id -p Result "$unit" 2>/dev/null
        done
        true
        """;

    // On Windows, Git may check out the source with CRLF, and bash treats \r as part of the command.
    public static string Command { get; } =
        "bash -c " + ShellQuote.Q(Script.ReplaceLineEndings("\n").Replace("<<DOCKER_FORMAT>>", ShellQuote.Q(DockerFormat)));
}