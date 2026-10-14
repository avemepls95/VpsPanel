using Renci.SshNet.Common;
using VpsPanel.Settings;

namespace VpsPanel.Ssh;

/// <summary>
/// Server key check using the trust on first use principle.
/// </summary>
internal sealed class HostKeyVerifier(ServerSettings server, ISettingsStore settings)
{
    public bool Mismatch { get; private set; }

    public string? ReceivedFingerprint { get; private set; }

    public void Handle(object? sender, HostKeyEventArgs e)
    {
        var fingerprint = "SHA256:" + e.FingerPrintSHA256;
        ReceivedFingerprint = fingerprint;

        if (server.HostKeyFingerprint is null)
        {
            settings.Update(s =>
            {
                var stored = s.Servers.FirstOrDefault(x => x.Id == server.Id);
                if (stored is not null && stored.HostKeyFingerprint is null)
                    stored.HostKeyFingerprint = fingerprint;
            });
            server.HostKeyFingerprint = fingerprint;
            e.CanTrust = true;
            return;
        }

        Mismatch = server.HostKeyFingerprint != fingerprint;
        e.CanTrust = !Mismatch;
    }
}