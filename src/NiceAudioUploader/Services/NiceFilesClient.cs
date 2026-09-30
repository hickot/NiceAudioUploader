using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NiceAudioUploader.Configuration;
using NiceAudioUploader.Models;

namespace NiceAudioUploader.Services;

public sealed class NiceFilesClient(IHttpClientFactory clients, INiceAuthenticationService authentication,
    IOptions<NiceOptions> options, HttpRetry retry) : INiceFilesClient
{
    public async Task<NiceFileUploadResponse> UploadAsync(NiceFileUploadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var client = clients.CreateClient("Nice");
            for (var refresh = 0; refresh <= 1; refresh++)
            {
                var token = await authentication.GetTokenAsync(cancellationToken, forceRefresh: refresh == 1);
                using var response = await retry.SendAsync(client, () =>
                {
                    var message = new HttpRequestMessage(HttpMethod.Post, options.Value.FilesUri) { Content = JsonContent.Create(request) };
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    return message;
                }, allowRetry: request.Overwrite, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized && refresh == 0) continue;
                if (!response.IsSuccessStatusCode)
                    return new(false, (int)response.StatusCode, HttpRetry.HttpError(response.StatusCode));
                if (response.StatusCode == HttpStatusCode.Accepted)
                    return new(false, 202, "HTTP 202: envio aceito, mas conclusão não confirmada; verifique na NICE.");

                // Reference curl contains no response schema. Trust HTTP completion, validate
                // JSON when present, and do not invent a success field or log the response body.
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        using var json = JsonDocument.Parse(body);
                        if (json.RootElement.ValueKind != JsonValueKind.Object)
                            return new(false, (int)response.StatusCode, "Resposta NICE inesperada; esperado objeto JSON. Verifique o destino antes de reenviar.");
                    }
                    catch (JsonException)
                    {
                        return new(false, (int)response.StatusCode, "Resposta NICE com JSON inválido. Verifique o destino antes de reenviar.");
                    }
                }
                return new(true, (int)response.StatusCode, null);
            }
            throw new InvalidOperationException("Unreachable");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or UserVisibleException)
        {
            return new(false, null, SafeErrors.Describe(error));
        }
    }
}
