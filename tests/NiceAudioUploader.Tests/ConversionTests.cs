using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NiceAudioUploader.Configuration;
using NiceAudioUploader.Services;

namespace NiceAudioUploader.Tests;

public sealed class ConversionTests
{
    [Fact]
    public void ValidatesWavAndRejectsEmptyCorruptedOrWrongCodec()
    {
        using var folder = new TempFolder();
        var path = folder.Add("test.wav");
        File.WriteAllBytes(path, ProcessorTests.WavBytes());
        WavValidator.Validate(path);
        var wrong = ProcessorTests.WavBytes();
        wrong[20] = 1;
        File.WriteAllBytes(path, wrong);
        Assert.Throws<UserVisibleException>(() => WavValidator.Validate(path));
        File.WriteAllText(path, "");
        Assert.Throws<UserVisibleException>(() => WavValidator.Validate(path));
    }

    [Fact]
    public async Task MissingExecutableAndOriginalAsOutputAreRejected()
    {
        using var folder = new TempFolder();
        var converter = new FfmpegAudioConverter(Options.Create(new FfmpegOptions { ExecutablePath = Path.Combine(folder.Path, "missing") }));
        await Assert.ThrowsAsync<UserVisibleException>(() => converter.EnsureAvailableAsync(default));
        var file = folder.Add("original.wav");
        await Assert.ThrowsAsync<UserVisibleException>(() => converter.ConvertAsync(file, file, default));
        Assert.Equal("synthetic", File.ReadAllText(file));
    }

    [FfmpegFact]
    public async Task AllSupportedFormatsConvertAndOriginalsRemainUntouched()
    {
        using var folder = new TempFolder();
        var converter = Converter();
        await converter.EnsureAvailableAsync(default);
        foreach (var extension in AudioFileScanner.SupportedExtensions)
        {
            var input = Path.Combine(folder.Path, "áudio com espaços" + extension);
            await RunAsync("ffmpeg", folder.Path, "-nostdin", "-v", "error", "-f", "lavfi", "-i",
                "sine=frequency=440:sample_rate=44100:duration=0.2", "-ac", "2", input);
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(input));
            var output = Path.Combine(folder.Path, Guid.NewGuid() + ".wav");
            await converter.ConvertAsync(input, output, default);
            await AssertFormatAsync(output, folder.Path);
            Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(input)));
            Assert.Equal(await File.ReadAllBytesAsync(output), Convert.FromBase64String(Convert.ToBase64String(await File.ReadAllBytesAsync(output))));
        }
    }

    [FfmpegFact]
    public async Task InvalidAudioReturnsConversionFailureWithoutChangingOriginal()
    {
        using var folder = new TempFolder();
        var input = folder.Add("corrupt.mp3", "invalid-audio");
        var error = await Assert.ThrowsAsync<UserVisibleException>(() => Converter().ConvertAsync(input, Path.Combine(folder.Path, "out.wav"), default));
        Assert.Contains("ExitCode", error.Message);
        Assert.Equal("invalid-audio", File.ReadAllText(input));
    }

    [ReferenceFact]
    public async Task MatchesEncodeJsFunctionalOutputUsingSameSyntheticInput()
    {
        using var folder = new TempFolder();
        var input = Path.Combine(folder.Path, "audio.wav");
        await RunAsync("ffmpeg", folder.Path, "-nostdin", "-v", "error", "-f", "lavfi", "-i",
            "sine=frequency=700:sample_rate=44100:duration=0.3", "-ac", "2", input);
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(input));
        await RunAsync("node", folder.Path, Path.GetFullPath(Environment.GetEnvironmentVariable("NICE_ENCODE_JS")!));
        var reference = Path.Combine(folder.Path, "audio_ulaw.wav");
        var converted = Path.Combine(folder.Path, "dotnet.wav");
        await Converter().ConvertAsync(input, converted, default);
        await AssertFormatAsync(reference, folder.Path);
        await AssertFormatAsync(converted, folder.Path);
        Assert.Equal(await File.ReadAllBytesAsync(reference), Convert.FromBase64String(await File.ReadAllTextAsync(Path.Combine(folder.Path, "base64Audio.txt"))));
        // Compare decoded samples (not WAV metadata/header bytes).
        foreach (var source in new[] { reference, converted })
            await RunAsync("ffmpeg", folder.Path, "-nostdin", "-v", "error", "-i", source, "-f", "s16le", source + ".pcm");
        Assert.Equal(await File.ReadAllBytesAsync(reference + ".pcm"), await File.ReadAllBytesAsync(converted + ".pcm"));
        Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(input)));
    }

    [FfmpegFact]
    public async Task CancellationTerminatesAnActiveFfmpegConversion()
    {
        using var folder = new TempFolder();
        var input = Path.Combine(folder.Path, "long.wav");
        var output = Path.Combine(folder.Path, "converted.wav");
        using (var stream = File.Create(input))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(ProcessorTests.WavBytes());
            stream.Position = 4;
            writer.Write(8_000_036u);
            stream.Position = 40;
            writer.Write(8_000_000u);
            stream.SetLength(8_000_044);
        }
        using var cts = new CancellationTokenSource();
        var conversion = Converter().ConvertAsync(input, output, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conversion.WaitAsync(TimeSpan.FromSeconds(10)));
        // The converter returns only after the process and both output readers exit.
        // Exclusive access and immediate deletion also exercise cleanup on Windows.
        if (File.Exists(output))
        {
            using (File.Open(output, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            File.Delete(output);
        }
        Assert.Equal(8_000_044, new FileInfo(input).Length);
    }

    private static FfmpegAudioConverter Converter() => new(Options.Create(new FfmpegOptions()));

    private static async Task AssertFormatAsync(string path, string folder)
    {
        WavValidator.Validate(path);
        var text = await RunAsync("ffprobe", folder, "-v", "error", "-show_entries", "format=format_name:stream=codec_name,sample_rate,channels", "-of", "json", path);
        using var json = JsonDocument.Parse(text);
        Assert.Equal("wav", json.RootElement.GetProperty("format").GetProperty("format_name").GetString());
        var stream = json.RootElement.GetProperty("streams")[0];
        Assert.Equal("pcm_mulaw", stream.GetProperty("codec_name").GetString());
        Assert.Equal("8000", stream.GetProperty("sample_rate").GetString());
        Assert.Equal(1, stream.GetProperty("channels").GetInt32());
    }

    private static async Task<string> RunAsync(string executable, string folder, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(true); await process.WaitForExitAsync(); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}

public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NICE_TEST_FFMPEG") != "1")
            Skip = "Defina NICE_TEST_FFMPEG=1 com ffmpeg e ffprobe no PATH para integração local.";
    }
}

public sealed class ReferenceFactAttribute : FactAttribute
{
    public ReferenceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NICE_TEST_FFMPEG") != "1" || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NICE_ENCODE_JS")))
            Skip = "Defina NICE_TEST_FFMPEG=1 e NICE_ENCODE_JS com o caminho da referência; requer Node.js.";
    }
}
