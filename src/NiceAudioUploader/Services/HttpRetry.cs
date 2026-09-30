using System.Net;
using Microsoft.Extensions.Options;
using NiceAudioUploader.Configuration;

namespace NiceAudioUploader.Services;

public sealed class HttpRetry(IOptions<NiceOptions> options, TimeProvider clock)
{
    public async Task<HttpResponseMessage> SendAsync(HttpClient client, Func<HttpRequestMessage> createRequest,
        bool allowRetry, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = createRequest();
            // No transport/timeout retry: the server might already have committed the upload.
            var response = await client.SendAsync(request, cancellationToken);
            var transient = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            if (!allowRetry || !transient || attempt >= options.Value.MaxRetries) return response;
            var retryAfter = response.Headers.RetryAfter;
            var delay = retryAfter?.Delta ?? (retryAfter?.Date - clock.GetUtcNow()) ??
                TimeSpan.FromSeconds(Math.Pow(2, attempt));
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
            // Never retry earlier than Retry-After; stop if it exceeds our bounded wait.
            if (delay > TimeSpan.FromSeconds(options.Value.MaxRetryDelaySeconds)) return response;
            response.Dispose();
            await Task.Delay(delay, clock, cancellationToken);
        }
    }

    public static string HttpError(HttpStatusCode code) => (int)code switch
    {
        400 => "HTTP 400: request rejeitado pela NICE.",
        401 => "HTTP 401: autenticação inválida ou token expirado.",
        403 => "HTTP 403: acesso negado; verifique as permissões NICE.",
        404 => "HTTP 404: endpoint ou destino não encontrado.",
        429 => "HTTP 429: limite de chamadas atingido; aguarde antes de tentar novamente.",
        >= 500 => $"HTTP {(int)code}: falha do servidor NICE após as tentativas permitidas.",
        _ => $"HTTP {(int)code}: resposta inesperada da NICE."
    };
}
