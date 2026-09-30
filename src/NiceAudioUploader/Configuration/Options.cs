namespace NiceAudioUploader.Configuration;

public sealed class NiceOptions
{
    public string AuthenticationBaseUrl { get; set; } = "";
    public string AuthenticationPath { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "";
    public string FilesPath { get; set; } = "";
    public string AccessKeyId { get; set; } = "";
    public string AccessKeySecret { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxRetries { get; set; } = 2;
    public int MaxRetryDelaySeconds { get; set; } = 60;

    public Uri AuthenticationUri => BuildUri(AuthenticationBaseUrl, AuthenticationPath);
    public Uri FilesUri => BuildUri(ApiBaseUrl, FilesPath);

    public void Validate()
    {
        _ = AuthenticationUri;
        _ = FilesUri;
        if (TimeoutSeconds is < 1 or > 3600 || MaxRetries is < 0 or > 5 ||
            MaxRetryDelaySeconds is < 1 or > 3600)
            throw new UserVisibleException("Configuração NICE inválida: revise timeout e limites de retry.");
    }

    private static Uri BuildUri(string baseUrl, string path)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var root) || root.Scheme != "https" ||
            !string.IsNullOrEmpty(root.UserInfo) || root.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(root.Query) || !string.IsNullOrEmpty(root.Fragment) ||
            !path.StartsWith('/') || path.StartsWith("//") || path.Contains('\\') ||
            path.Contains('?') || path.Contains('#'))
            throw new UserVisibleException("Configuração NICE inválida: informe hosts HTTPS e paths locais iniciados por /.");
        return new Uri(root, path);
    }
}

public sealed class FfmpegOptions
{
    public string ExecutablePath { get; set; } = "ffmpeg";
    public int TimeoutSeconds { get; set; } = 300;
}

public sealed class AudioProcessingOptions
{
    public bool Overwrite { get; set; } = true;
}
