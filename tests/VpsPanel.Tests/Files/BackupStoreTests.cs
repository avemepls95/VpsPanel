using VpsPanel.Files;
using VpsPanel.Settings;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Files;

public sealed class BackupStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 14, 5, 12, TimeSpan.Zero);
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact(DisplayName = "Server name with invalid characters => characters replaced with _, the path repeats the path on the server")]
    public async Task Save_Should_SanitizeServerName_When_NameHasInvalidChars()
    {
        // Arrange
        var store = new BackupStore(new SettingsPaths(_dir.Path), new FixedTimeProvider(Now));

        // Act
        var path = await store.Save("a:b/c?", "/root/app/.env", "old"u8.ToArray(), CancellationToken.None);

        // Assert
        path.Should().Be(Path.Combine(_dir.Path, "backups", "a_b_c_", "20261013-140512", "root", "app", ".env"));
        (await File.ReadAllTextAsync(path)).Should().Be("old");
    }

    [Fact(DisplayName = "Two copies of one file in the same second => both saved to different files")]
    public async Task Save_Should_KeepBothCopies_When_SameSecond()
    {
        // Arrange
        var store = new BackupStore(new SettingsPaths(_dir.Path), new FixedTimeProvider(Now));
        var first = await store.Save("Main", "/root/.env", "first"u8.ToArray(), CancellationToken.None);

        // Act
        var second = await store.Save("Main", "/root/.env", "second"u8.ToArray(), CancellationToken.None);

        // Assert
        second.Should().Be(first + ".2");
        (await File.ReadAllTextAsync(first)).Should().Be("first");
        (await File.ReadAllTextAsync(second)).Should().Be("second");
    }
}