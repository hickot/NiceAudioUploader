using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using NiceAudioUploader.Configuration;

namespace NiceAudioUploader.Tests;

internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("nice-tests-").FullName;
    public string Add(string name, string content = "synthetic")
    {
        var file = System.IO.Path.Combine(Path, name);
        File.WriteAllText(file, content);
        return file;
    }
    public void Dispose() => Directory.Delete(Path, true);
}

internal sealed class StubHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, ++Calls, cancellationToken);
    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

internal sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan time) => now += time;
}

internal static class TestOptions
{
    public static IOptions<NiceOptions> Nice(int retries = 0) => Options.Create(new NiceOptions
    {
        AuthenticationBaseUrl = "https://auth.example.test", AuthenticationPath = "/authentication/v1/token/access-key",
        ApiBaseUrl = "https://api.example.test", FilesPath = "/incontactapi/services/v33.0/files",
        AccessKeyId = "synthetic-id", AccessKeySecret = "synthetic-secret", MaxRetries = retries
    });
}
