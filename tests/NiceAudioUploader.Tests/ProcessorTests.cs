using Microsoft.Extensions.Logging.Abstractions;
using NiceAudioUploader.Models;
using NiceAudioUploader.Services;

namespace NiceAudioUploader.Tests;

public sealed class ProcessorTests
{
    [Fact]
    public async Task ContinuesAfterConversionAndUploadFailuresEncodesWholeFileAndCleansTemp()
    {
        using var folder = new TempFolder();
        var files = new[] { folder.Add("bad.wav"), folder.Add("denied.mp3"), folder.Add("saudação final.wav") };
        var converter = new FakeConverter((input, output, ct) =>
            Path.GetFileName(input) == "bad.wav" ? throw new UserVisibleException("Conversão inválida.") : File.WriteAllBytesAsync(output, WavBytes(), ct));
        var auth = new FakeAuthentication();
        var requests = new List<NiceFileUploadRequest>();
        var client = new FakeClient((request, _) =>
        {
            requests.Add(request);
            return Task.FromResult(request.FileName.Contains("denied") ? new NiceFileUploadResponse(false, 403, "HTTP 403") : new(true, 200, null));
        });
        var result = await Processor(converter, auth, client).ProcessAsync(new(NiceEnvironment.HML, folder.Path, files, true), default);
        Assert.False(result.Cancelled);
        Assert.Equal(new[] { false, false, true }, result.Files.Select(f => f.Success));
        Assert.Equal(1, auth.Calls);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, request =>
        {
            Assert.StartsWith("Nice/PVI/Prompts/HML/", request.FileName);
            Assert.Equal(WavBytes(), Convert.FromBase64String(request.File));
        });
        Assert.All(converter.Outputs, path => Assert.False(Directory.Exists(Path.GetDirectoryName(path))));
        Assert.All(files, file => Assert.Equal("synthetic", File.ReadAllText(file)));
    }

    [Fact]
    public async Task EmptyBatchDoesNotAuthenticateOrConvert()
    {
        using var folder = new TempFolder();
        var converter = new FakeConverter((_, _, _) => throw new Exception());
        var auth = new FakeAuthentication();
        var result = await Processor(converter, auth, new FakeClient((_, _) => throw new Exception()))
            .ProcessAsync(new(NiceEnvironment.DEV, folder.Path, [], true), default);
        Assert.Empty(result.Files);
        Assert.Equal(0, auth.Calls);
        Assert.Equal(0, converter.AvailabilityChecks);
    }

    [Fact]
    public async Task CancellationStopsRemainingFilesAndRemovesPartialOutput()
    {
        using var folder = new TempFolder();
        using var cts = new CancellationTokenSource();
        var converter = new FakeConverter(async (_, output, ct) =>
        {
            await File.WriteAllTextAsync(output, "partial", ct);
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
        });
        var result = await Processor(converter, new FakeAuthentication(), new FakeClient((_, _) => throw new Exception()))
            .ProcessAsync(new(NiceEnvironment.DEV, folder.Path, [folder.Add("a.wav"), folder.Add("b.wav")], true), cts.Token);
        Assert.True(result.Cancelled);
        Assert.Single(result.Files);
        Assert.False(result.Files[0].Success);
        Assert.Single(converter.Outputs);
        Assert.False(Directory.Exists(Path.GetDirectoryName(converter.Outputs[0])));
    }

    [Fact]
    public async Task MissingFfmpegPreventsAuthentication()
    {
        using var folder = new TempFolder();
        var converter = new FakeConverter((_, _, _) => throw new Exception()) { AvailabilityError = new UserVisibleException("FFmpeg ausente.") };
        var auth = new FakeAuthentication();
        var result = await Processor(converter, auth, new FakeClient((_, _) => throw new Exception()))
            .ProcessAsync(new(NiceEnvironment.DEV, folder.Path, [folder.Add("a.wav")], true), default);
        Assert.Equal(0, auth.Calls);
        Assert.False(Assert.Single(result.Files).Success);
    }

    [Fact]
    public async Task CollisionPreventsAllSideEffects()
    {
        using var folder = new TempFolder();
        var converter = new FakeConverter((_, _, _) => throw new Exception());
        var auth = new FakeAuthentication();
        await Assert.ThrowsAsync<UserVisibleException>(() => Processor(converter, auth, new FakeClient((_, _) => throw new Exception()))
            .ProcessAsync(new(NiceEnvironment.DEV, folder.Path, [folder.Add("a.wav"), folder.Add("a.mp3")], true), default));
        Assert.Equal(0, auth.Calls);
        Assert.Equal(0, converter.AvailabilityChecks);
    }

    internal static byte[] WavBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(38u); writer.Write("WAVEfmt "u8); writer.Write(16u);
        writer.Write((ushort)7); writer.Write((ushort)1); writer.Write(8000u); writer.Write(8000u);
        writer.Write((ushort)1); writer.Write((ushort)8); writer.Write("data"u8); writer.Write(2u);
        writer.Write(new byte[] { 0xff, 0xff });
        return stream.ToArray();
    }

    private static AudioProcessor Processor(IAudioConverter converter, INiceAuthenticationService auth, INiceFilesClient client) =>
        new(converter, auth, client, new NicePathBuilder(), NullLogger<AudioProcessor>.Instance);
}

internal sealed class FakeConverter(Func<string, string, CancellationToken, Task> convert) : IAudioConverter
{
    public List<string> Outputs { get; } = [];
    public int AvailabilityChecks { get; private set; }
    public Exception? AvailabilityError { get; init; }
    public Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        AvailabilityChecks++;
        return AvailabilityError is null ? Task.CompletedTask : Task.FromException(AvailabilityError);
    }
    public Task ConvertAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        Outputs.Add(outputPath);
        return convert(inputPath, outputPath, cancellationToken);
    }
}

internal sealed class FakeClient(Func<NiceFileUploadRequest, CancellationToken, Task<NiceFileUploadResponse>> upload) : INiceFilesClient
{
    public Task<NiceFileUploadResponse> UploadAsync(NiceFileUploadRequest request, CancellationToken cancellationToken) => upload(request, cancellationToken);
}
