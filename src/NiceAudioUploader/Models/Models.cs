using System.Text.Json.Serialization;

namespace NiceAudioUploader.Models;

public enum NiceEnvironment { DEV, HML }

public sealed class NiceTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("expires_in")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? ExpiresIn { get; init; }
}

public sealed class NiceFileUploadRequest
{
    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }
    [JsonPropertyName("file")]
    public required string File { get; init; }
    [JsonPropertyName("overwrite")]
    public bool Overwrite { get; init; }
}

public sealed record NiceFileUploadResponse(bool Success, int? StatusCode, string? Error);
public sealed record AudioProcessingResult(string FileName, string RemotePath, bool Success, string? Error);
public sealed record BatchResult(IReadOnlyList<AudioProcessingResult> Files, bool Cancelled);

// Own the snapshot: neither the environment nor the input list can change during the batch.
public sealed class AudioBatch
{
    public NiceEnvironment Environment { get; }
    public string Folder { get; }
    public IReadOnlyList<string> Files { get; }
    public bool Overwrite { get; }

    public AudioBatch(NiceEnvironment environment, string folder, IEnumerable<string> files, bool overwrite)
    {
        if (!Enum.IsDefined(environment)) throw new ArgumentOutOfRangeException(nameof(environment));
        Environment = environment;
        Folder = Path.GetFullPath(folder);
        Files = Array.AsReadOnly(files.Select(Path.GetFullPath).ToArray());
        Overwrite = overwrite;
    }
}
