using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using VpsPanel.Settings;

namespace VpsPanel.Tests.Infrastructure;

internal sealed class SshServerContainer : IAsyncDisposable
{
    private static readonly SemaphoreSlim ImageGate = new(1, 1);
    private static IFutureDockerImage? _image;

    private readonly IContainer _container;

    private SshServerContainer(IContainer container, string keyPath)
    {
        _container = container;
        KeyPath = keyPath;
    }

    public string KeyPath { get; }

    public static async Task<SshServerContainer> Start(bool dockerStub)
    {
        var image = await GetImage();
        var keys = TestKeys.Create();
        var keyPath = Path.Combine(Path.GetTempPath(), $"vpspanel-test-key-{Guid.NewGuid():N}.pem");
        await File.WriteAllTextAsync(keyPath, keys.PrivateKeyPem);

        var container = new ContainerBuilder(image)
            .WithPortBinding(22, true)
            .WithEnvironment("AUTHORIZED_KEY", keys.AuthorizedKey)
            .WithEnvironment("DOCKER_STUB", dockerStub ? "1" : "0")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server listening on"))
            .Build();
        await container.StartAsync();
        return new SshServerContainer(container, keyPath);
    }

    public ServerSettings Server(string name = "test") => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Host = _container.Hostname,
        Port = _container.GetMappedPublicPort(22),
        User = "root",
        KeyPath = KeyPath
    };

    /// <summary>
    /// A server that is reached through the fake aws, which forwards connections to this container.
    /// </summary>
    public ServerSettings SsmServer(string name = "test") => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Host = "",
        Port = 22,
        User = "root",
        KeyPath = KeyPath,
        Ssm = new SsmSettings("i-0123456789abcdef0", null, null)
    };

    /// <summary>
    /// Runs a command in the container through docker exec, bypassing the SSH code under test.
    /// </summary>
    public async Task<string> Run(string bashCommand)
    {
        // On Windows, multiline test scripts may come from sources with CRLF.
        var result = await _container.ExecAsync(["bash", "-c", bashCommand.ReplaceLineEndings("\n")]);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"The command exited with code {result.ExitCode}: {result.Stderr}");
        return result.Stdout;
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        File.Delete(KeyPath);
    }

    private static async Task<IFutureDockerImage> GetImage()
    {
        await ImageGate.WaitAsync();
        try
        {
            if (_image is null)
            {
                var image = new ImageFromDockerfileBuilder()
                    .WithDockerfileDirectory(CommonDirectoryPath.GetProjectDirectory(), "SshServer")
                    .WithDockerfile("Dockerfile")
                    .WithName("vpspanel-test-sshd:latest")
                    .Build();
                await image.CreateAsync();
                _image = image;
            }
            return _image;
        }
        finally
        {
            ImageGate.Release();
        }
    }
}