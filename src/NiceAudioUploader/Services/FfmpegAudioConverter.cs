using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using NiceAudioUploader.Configuration;

namespace NiceAudioUploader.Services;

public sealed class FfmpegAudioConverter(IOptions<FfmpegOptions> options) : IAudioConverter
{
    public async Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(["-version"], cancellationToken);
        if (result.ExitCode != 0 || !result.Stdout.StartsWith("ffmpeg version", StringComparison.OrdinalIgnoreCase))
            throw new UserVisibleException("O executável configurado não respondeu como FFmpeg.");
    }

    public async Task ConvertAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new UserVisibleException("A saída temporária não pode ser o arquivo original.");
        if (!AudioFileScanner.SupportedExtensions.Contains(Path.GetExtension(inputPath), StringComparer.OrdinalIgnoreCase))
            throw new UserVisibleException("Extensão de áudio não suportada.");
        var info = new FileInfo(inputPath);
        if (!info.Exists || info.Length == 0) throw new UserVisibleException("Arquivo original inexistente ou vazio.");
        // -n additionally protects any existing file if this service is called independently.
        var result = await RunAsync(["-nostdin", "-hide_banner", "-loglevel", "error", "-n", "-i",
            Path.GetFullPath(inputPath), "-vn", "-ar", "8000", "-ac", "1", "-acodec", "pcm_mulaw",
            "-f", "wav", Path.GetFullPath(outputPath)], cancellationToken);
        if (result.ExitCode != 0)
            throw new UserVisibleException($"Falha na conversão: FFmpeg retornou ExitCode {result.ExitCode}. Verifique o áudio e as permissões.");
        WavValidator.Validate(outputPath);
    }

    private async Task<ProcessResult> RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (options.Value.TimeoutSeconds is < 1 or > 86400)
            throw new UserVisibleException("Ffmpeg:TimeoutSeconds deve estar entre 1 e 86400.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        var start = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(options.Value.ExecutablePath) ? "ffmpeg" : options.Value.ExecutablePath,
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Win32Exception)
        {
            throw new UserVisibleException("FFmpeg não encontrado ou não executável. Configure Ffmpeg:ExecutablePath ou adicione o FFmpeg ao PATH.");
        }

        // Drain both streams concurrently, retaining only bounded diagnostics in memory.
        var stdout = CaptureAsync(process.StandardOutput);
        var stderr = CaptureAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* Already exited. */ }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            cancellationToken.ThrowIfCancellationRequested();
            throw new UserVisibleException("Tempo limite excedido durante a execução do FFmpeg.");
        }
    }

    private static async Task<string> CaptureAsync(StreamReader reader)
    {
        var retained = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
            if (retained.Length < 16384) retained.Append(buffer, 0, Math.Min(count, 16384 - retained.Length));
        return retained.ToString();
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
