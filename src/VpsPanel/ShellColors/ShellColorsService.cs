using VpsPanel.Files;
using VpsPanel.Settings;
using VpsPanel.Ssh;
using static VpsPanel.Ssh.ShellQuote;

namespace VpsPanel.ShellColors;

/// <summary>
/// Turns on a colored prompt and colored ls and grep in ~/.bashrc of the server user.
/// </summary>
internal sealed class ShellColorsService(ISettingsStore settings, ISshConnector connector, BackupStore backups)
{
    public const string BlockStart = "# >>> VpsPanel colors";
    public const string BlockEnd = "# <<< VpsPanel colors";

    private static readonly string Block = $"""
        {BlockStart}
        # Added by VpsPanel, the "Disable colors" button in the panel removes this block.
        if [[ $- == *i* ]]; then
          PS1='\[\e[01;32m\]\u@\h\[\e[00m\]:\[\e[01;34m\]\w\[\e[00m\]\$ '
          eval "$(dircolors -b)"
          alias ls='ls --color=auto'
          alias grep='grep --color=auto'
        fi
        {BlockEnd}
        """;

    // The commands go through the login shell of the user, which may be fish, so they are wrapped in sh.
    private static readonly string StateScript = $"""
        case "$(getent passwd "$(id -un)" | cut -d: -f7)" in
          */bash) ;;
          *) echo unavailable; exit 0 ;;
        esac
        if grep -qxF {Q(BlockStart)} ~/.bashrc 2>/dev/null; then echo on; else echo off; fi
        """;

    public async Task<ShellColorsView> Get(string serverId, CancellationToken ct)
    {
        await using var session = await connector.Connect(settings.Read().GetServer(serverId), ct);
        return new ShellColorsView(await ReadState(session, ct));
    }

    public async Task<ShellColorsView> Enable(string serverId, CancellationToken ct)
    {
        var server = settings.Read().GetServer(serverId);
        await using var session = await connector.Connect(server, ct);
        var state = await ReadState(session, ct);
        if (state == ShellColorsState.Unavailable)
            throw new PanelException(409, "Colors can be turned on only when the login shell on the server is bash.");
        if (state == ShellColorsState.On)
            return new ShellColorsView(state);

        await Backup(session, server.Name, ct);
        await Sh(session, $"""
            set -e
            touch ~/.bashrc
            if [ -s ~/.bashrc ] && [ -n "$(tail -c 1 ~/.bashrc)" ]; then echo >> ~/.bashrc; fi
            printf '%s\n' {Q(Block)} >> ~/.bashrc
            """, ct);
        return new ShellColorsView(ShellColorsState.On);
    }

    public async Task<ShellColorsView> Disable(string serverId, CancellationToken ct)
    {
        var server = settings.Read().GetServer(serverId);
        await using var session = await connector.Connect(server, ct);
        await Backup(session, server.Name, ct);
        await Sh(session, $"""
            if [ -f ~/.bashrc ]; then
              sed -i --follow-symlinks {Q($"/^{BlockStart}$/,/^{BlockEnd}$/d")} ~/.bashrc
            fi
            """, ct);
        return new ShellColorsView(await ReadState(session, ct));
    }

    private static async Task<ShellColorsState> ReadState(ISshSession session, CancellationToken ct) =>
        (await Sh(session, StateScript, ct)).Trim() switch
        {
            "on" => ShellColorsState.On,
            "off" => ShellColorsState.Off,
            _ => ShellColorsState.Unavailable
        };

    private async Task Backup(ISshSession session, string serverName, CancellationToken ct)
    {
        var bashrc = await Sh(session, "printf '%s/.bashrc' \"$HOME\"", ct);
        if (await RemoteFs.StatFollow(session, bashrc, ct) != 'f')
            return;
        await using var content = await session.OpenRead(bashrc, ct);
        await backups.Save(serverName, bashrc, content, ct);
    }

    private static async Task<string> Sh(ISshSession session, string script, CancellationToken ct)
    {
        var result = await session.Exec("sh -c " + Q(script.ReplaceLineEndings("\n")), ct);
        if (result.ExitCode != 0)
            throw new PanelException(500, result.Stderr.Trim());
        return result.Stdout;
    }
}