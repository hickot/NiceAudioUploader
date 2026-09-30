using NiceAudioUploader.Models;
using NiceAudioUploader.Services;

namespace NiceAudioUploader.Tests;

public sealed class PathsAndScannerTests
{
    [Theory]
    [InlineData(NiceEnvironment.DEV, "teste.wav", "Nice/PVI/Prompts/DEV/teste.wav")]
    [InlineData(NiceEnvironment.HML, "teste.wav", "Nice/PVI/Prompts/HML/teste.wav")]
    [InlineData(NiceEnvironment.DEV, "saudação inicial.MP3", "Nice/PVI/Prompts/DEV/saudação inicial.wav")]
    [InlineData(NiceEnvironment.HML, "menu.v2.m4a", "Nice/PVI/Prompts/HML/menu.v2.wav")]
    public void BuildsPortableRemotePath(NiceEnvironment environment, string name, string expected) =>
        Assert.Equal(expected, new NicePathBuilder().BuildRemotePath(environment, name));

    [Fact]
    public void RejectsInvalidEnvironment() => Assert.Throws<ArgumentOutOfRangeException>(() =>
        new NicePathBuilder().BuildRemotePath((NiceEnvironment)9, "audio.wav"));

    [Theory]
    [InlineData("../audio.wav")]
    [InlineData("a\\audio.wav")]
    [InlineData(".wav")]
    [InlineData("a\na.wav")]
    [InlineData("a:b.wav")]
    public void RejectsUnsafeFileNames(string name) => Assert.Throws<UserVisibleException>(() =>
        new NicePathBuilder().BuildRemotePath(NiceEnvironment.DEV, name));

    [Fact]
    public void ScansOnlySupportedTopLevelFilesCaseInsensitively()
    {
        using var folder = new TempFolder();
        var names = new[] { "a.wav", "b.WAV", "c.Wav", "d.mp3", "e.MP3", "f.m4a", "g.aac", "h.flac", "i.ogg" };
        foreach (var name in names) folder.Add(name);
        folder.Add("ignored.txt");
        Directory.CreateDirectory(Path.Combine(folder.Path, "child"));
        folder.Add(Path.Combine("child", "nested.wav"));
        Assert.Equal(names, new AudioFileScanner().Scan(folder.Path).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsEmptyPath(string folder) => Assert.Throws<UserVisibleException>(() => new AudioFileScanner().Scan(folder));

    [Fact]
    public void RejectsMissingAndEmptyFolders()
    {
        using var folder = new TempFolder();
        var scanner = new AudioFileScanner();
        Assert.Throws<UserVisibleException>(() => scanner.Scan(folder.Path));
        folder.Add("not-audio.txt");
        Assert.Throws<UserVisibleException>(() => scanner.Scan(folder.Path));
        Assert.Throws<UserVisibleException>(() => scanner.Scan(Path.Combine(folder.Path, "missing")));
    }

    [Fact]
    public void RejectsCollisionsAndSnapshotsBatch()
    {
        using var folder = new TempFolder();
        var files = new[] { folder.Add("MENU.mp3"), folder.Add("menu.wav") };
        var batch = new AudioBatch(NiceEnvironment.HML, folder.Path, files, true);
        files[0] = "changed.wav";
        Assert.EndsWith("MENU.mp3", batch.Files[0]);
        Assert.Throws<UserVisibleException>(() => NicePathBuilder.ValidateUniqueDestinations(batch, new NicePathBuilder()));
    }

    [Fact]
    public void InaccessibleFolderIsReportedOnUnix()
    {
        if (OperatingSystem.IsWindows()) return; // ACL setup requires host-specific privileges.
        using var folder = new TempFolder();
        folder.Add("a.wav");
        var originalMode = File.GetUnixFileMode(folder.Path);
        try
        {
            File.SetUnixFileMode(folder.Path, UnixFileMode.None);
            Assert.Throws<UserVisibleException>(() => new AudioFileScanner().Scan(folder.Path));
        }
        finally { File.SetUnixFileMode(folder.Path, originalMode); }
    }

    [Theory]
    [InlineData("http://example.test")]
    [InlineData("https://user:password@example.test")]
    [InlineData("https://example.test/path")]
    public void RejectsUnsafeEndpoint(string endpoint)
    {
        var options = TestOptions.Nice().Value;
        options.ApiBaseUrl = endpoint;
        Assert.Throws<UserVisibleException>(options.Validate);
    }
}
