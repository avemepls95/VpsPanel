using VpsPanel.Files;
using VpsPanel.Runs;
using VpsPanel.Settings;
using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Files;

[Collection("two-ssh")]
public sealed class CopyServiceTests(TwoSshServersFixture fixture) : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private (CopyService Service, ServerSettings First, ServerSettings Second) Create()
    {
        var first = fixture.First.Server("First");
        var second = fixture.Second.Server("Second");
        var store = TestSettings.Store(_dir, first, second);
        var service = new CopyService(store, new SshConnector(store), new RunManager(TimeProvider.System),
            new BackupStore(new SettingsPaths(_dir.Path), TimeProvider.System));
        return (service, first, second);
    }

    private static IEnumerable<string> Lines(Run run) => run.ReadAfter(0).Lines.Select(l => l.Text);

    [Fact(DisplayName = "File to another server => content and permissions same as the source")]
    public async Task Start_Should_CopyFileWithMode_When_TargetFree()
    {
        // Arrange
        await fixture.First.Run("mkdir -p /tmp/src && printf content > /tmp/src/file.txt && chmod 640 /tmp/src/file.txt");
        await fixture.Second.Run("rm -rf /tmp/dst && mkdir -p /tmp/dst");
        var (service, first, second) = Create();

        // Act
        var run = await service.Start(new CopyRequest(first.Id, "/tmp/src/file.txt", second.Id, "/tmp/dst", false), CancellationToken.None);

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Succeeded);
        (await fixture.Second.Run("cat /tmp/dst/file.txt")).Should().Be("content");
        (await fixture.Second.Run("stat -c %a /tmp/dst/file.txt")).Should().Be("640\n");
    }

    [Fact(DisplayName = "Folder with a link, FIFO and a setuid file => link recreated, FIFO skipped, permissions kept")]
    public async Task Start_Should_CopyTree_When_FolderHasSpecialObjects()
    {
        // Arrange
        await fixture.First.Run("""
            rm -rf "/tmp/tree-src" && mkdir -p "/tmp/tree-src/my 'tree'/sub" && cd "/tmp/tree-src/my 'tree'"
            printf data > sub/data.txt
            printf '#!/bin/sh' > tool.sh && chmod 4755 tool.sh
            ln -s sub/data.txt link
            mkfifo pipe
            chmod 750 sub
            """);
        await fixture.Second.Run("rm -rf /tmp/tree-dst && mkdir -p /tmp/tree-dst");
        var (service, first, second) = Create();

        // Act
        var run = await service.Start(new CopyRequest(first.Id, "/tmp/tree-src/my 'tree'", second.Id, "/tmp/tree-dst", false), CancellationToken.None);

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Succeeded);
        var root = "\"/tmp/tree-dst/my 'tree'\"";
        (await fixture.Second.Run($"cat {root}/sub/data.txt")).Should().Be("data");
        (await fixture.Second.Run($"readlink {root}/link")).Should().Be("sub/data.txt\n");
        (await fixture.Second.Run($"stat -c %a {root}/tool.sh")).Should().Be("4755\n");
        (await fixture.Second.Run($"stat -c %a {root}/sub")).Should().Be("750\n");
        await fixture.Second.Run($"test ! -e {root}/pipe");
        Lines(run).Should().Contain("Skipped FIFO, socket or device: /tmp/tree-dst/my 'tree'/pipe");
    }

    [Fact(DisplayName = "Names with a space, $, a newline and a leading hyphen => all objects copied")]
    public async Task Start_Should_CopyTree_When_NamesHaveSpecialCharacters()
    {
        // Arrange
        await fixture.First.Run("""
            rm -rf /tmp/odd-src && mkdir -p '/tmp/odd-src/-dir/sub $dir' && cd /tmp/odd-src/-dir
            printf a > 'sp ace.txt'
            printf b > ./-dash.txt
            printf c > 'dollar$HOME.txt'
            printf d > $'new\nline.txt'
            printf e > 'sub $dir/-inner.txt'
            ln -s -- -dash.txt ./-link
            ln -s -- $'new\nline.txt' 'lf link'
            """);
        await fixture.Second.Run("rm -rf /tmp/odd-dst && mkdir -p /tmp/odd-dst");
        var (service, first, second) = Create();

        // Act
        var run = await service.Start(new CopyRequest(first.Id, "/tmp/odd-src/-dir", second.Id, "/tmp/odd-dst", false), CancellationToken.None);

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Succeeded);
        (await fixture.Second.Run("""cd /tmp/odd-dst/-dir && cat 'sp ace.txt' ./-dash.txt 'dollar$HOME.txt' $'new\nline.txt' 'sub $dir/-inner.txt'"""))
            .Should().Be("abcde");
        (await fixture.Second.Run("readlink /tmp/odd-dst/-dir/-link")).Should().Be("-dash.txt\n");
        (await fixture.Second.Run("readlink '/tmp/odd-dst/-dir/lf link'")).Should().Be("new\nline.txt\n");
    }

    [Fact(DisplayName = "File exists, overwrite confirmed => new content, same permissions, backup exists")]
    public async Task Start_Should_ReplaceFileAndBackup_When_Overwrite()
    {
        // Arrange
        await fixture.First.Run("mkdir -p /tmp/src && printf new > /tmp/src/app.env && chmod 644 /tmp/src/app.env");
        await fixture.Second.Run("mkdir -p /tmp/dst-replace && printf old > /tmp/dst-replace/app.env && chmod 600 /tmp/dst-replace/app.env");
        var (service, first, second) = Create();

        // Act
        var run = await service.Start(new CopyRequest(first.Id, "/tmp/src/app.env", second.Id, "/tmp/dst-replace", true), CancellationToken.None);

        // Assert
        await run.Completed;
        (await fixture.Second.Run("cat /tmp/dst-replace/app.env")).Should().Be("new");
        (await fixture.Second.Run("stat -c %a /tmp/dst-replace/app.env")).Should().Be("600\n");
        var backupLine = Lines(run).Single(l => l.StartsWith("Previous version saved to ", StringComparison.Ordinal));
        (await File.ReadAllTextAsync(backupLine["Previous version saved to ".Length..])).Should().Be("old");
    }

    [Fact(DisplayName = "File exists, overwrite not confirmed => 409 before the run starts")]
    public async Task Start_Should_Throw409_When_FileExistsWithoutOverwrite()
    {
        // Arrange
        await fixture.First.Run("mkdir -p /tmp/src && printf new > /tmp/src/conflict.env");
        await fixture.Second.Run("mkdir -p /tmp/dst-conflict && printf old > /tmp/dst-conflict/conflict.env");
        var (service, first, second) = Create();

        // Act
        var act = () => service.Start(new CopyRequest(first.Id, "/tmp/src/conflict.env", second.Id, "/tmp/dst-conflict", false), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage("The destination folder already has a file named conflict.env.")
            .Which.StatusCode.Should().Be(409);
    }

    [Fact(DisplayName = "A folder with this name exists => 409 even with overwrite confirmed")]
    public async Task Start_Should_Throw409_When_FolderExists()
    {
        // Arrange
        await fixture.First.Run("mkdir -p /tmp/src-dir/tree");
        await fixture.Second.Run("mkdir -p /tmp/dst-dir/tree");
        var (service, first, second) = Create();

        // Act
        var act = () => service.Start(new CopyRequest(first.Id, "/tmp/src-dir/tree", second.Id, "/tmp/dst-dir", true), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage("The destination folder already has a folder named tree.")
            .Which.StatusCode.Should().Be(409);
    }

    [Fact(DisplayName = "Folder into itself on the same server => 400")]
    public async Task Start_Should_Throw400_When_CopyIntoItself()
    {
        // Arrange
        await fixture.First.Run("mkdir -p /tmp/self/inner");
        var (service, first, _) = Create();

        // Act
        var act = () => service.Start(new CopyRequest(first.Id, "/tmp/self", first.Id, "/tmp/self/inner", false), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage("A folder cannot be copied into itself.")
            .Which.StatusCode.Should().Be(400);
    }

    [Fact(DisplayName = "File into its own folder on the same server => 400, file untouched")]
    public async Task Start_Should_Throw400_When_FileCopiedIntoOwnFolder()
    {
        // Arrange
        await fixture.First.Run("mkdir -p /tmp/same && printf keep > /tmp/same/file.txt");
        var (service, first, _) = Create();

        // Act
        var act = () => service.Start(new CopyRequest(first.Id, "/tmp/same/file.txt", first.Id, "/tmp/same", true), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<PanelException>())
            .WithMessage("The file is already in this folder.")
            .Which.StatusCode.Should().Be(400);
        (await fixture.First.Run("cat /tmp/same/file.txt")).Should().Be("keep");
    }
}