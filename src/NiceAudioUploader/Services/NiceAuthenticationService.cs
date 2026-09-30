using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NiceAudioUploader.Configuration;
using NiceAudioUploader.Models;

namespace NiceAudioUploader.Services;

public sealed class NiceAuthenticationService(IHttpClientFactory clients, IOptions<NiceOptions> options,
    HttpRetry retry, TimeProvider clock) : INiceAuthenticationService, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? token;
    private DateTimeOffset expiresAt;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && token is not null && clock.GetUtcNow() < expiresAt) return token;
            token = null;
            if (string.IsNullOrWhiteSpace(options.Value.AccessKeyId) || string.IsNullOrWhiteSpace(options.Value.AccessKeySecret))
                throw new UserVisibleException("Configure NICE_ACCESS_KEY_ID e NICE_ACCESS_KEY_SECRET ou .NET User Secrets.");
            using var client = clients.CreateClient("Nice");
            var issuedAt = clock.GetUtcNow();
            using var response = await retry.SendAsync(client, () => new HttpRequestMessage(HttpMethod.Post, options.Value.AuthenticationUri)
            {
                Content = JsonContent.Create(new { accessKeyId = options.Value.AccessKeyId, accessKeySecret = options.Value.AccessKeySecret })
            }, allowRetry: true, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new UserVisibleException("Autenticação: " + HttpRetry.HttpError(response.StatusCode));
            NiceTokenResponse? body;
            try { body = await response.Content.ReadFromJsonAsync<NiceTokenResponse>(cancellationToken); }
            catch (JsonException) { throw new UserVisibleException("Autenticação: JSON inesperado na resposta NICE."); }
            if (string.IsNullOrWhiteSpace(body?.AccessToken) || body.AccessToken.Any(char.IsWhiteSpace) || body.ExpiresIn is <= 0)
                throw new UserVisibleException("Autenticação: resposta sem token válido ou com expiração inválida.");
            // Missing expiry is not guessed: reuse in memory until a controlled 401 refresh.
            expiresAt = DateTimeOffset.MaxValue;
            if (body.ExpiresIn is { } seconds)
            {
                if (seconds > (DateTimeOffset.MaxValue - issuedAt).TotalSeconds)
                    throw new UserVisibleException("Autenticação: expiração fora do intervalo permitido.");
                expiresAt = issuedAt.AddSeconds(seconds - Math.Min(30, seconds * 0.1));
            }
            token = body.AccessToken;
            return token;
        }
        finally { gate.Release(); }
    }

    public void Dispose() => gate.Dispose();
}
