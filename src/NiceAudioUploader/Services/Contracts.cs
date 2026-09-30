using NiceAudioUploader.Models;

namespace NiceAudioUploader.Services;

public interface IFolderSelector { Task<string?> SelectFolderAsync(CancellationToken cancellationToken); }
public interface IAudioFileScanner { IReadOnlyList<string> Scan(string folder); }
public interface INicePathBuilder { string BuildRemotePath(NiceEnvironment environment, string audioFileName); }
public interface IAudioConverter
{
    Task EnsureAvailableAsync(CancellationToken cancellationToken);
    Task ConvertAsync(string inputPath, string outputPath, CancellationToken cancellationToken);
}
public interface INiceAuthenticationService
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken, bool forceRefresh = false);
}
public interface INiceFilesClient
{
    Task<NiceFileUploadResponse> UploadAsync(NiceFileUploadRequest request, CancellationToken cancellationToken);
}
public interface IAudioProcessor
{
    Task<BatchResult> ProcessAsync(AudioBatch batch, CancellationToken cancellationToken);
}
