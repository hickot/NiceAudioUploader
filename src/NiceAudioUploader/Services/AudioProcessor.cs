using Microsoft.Extensions.Logging;
using NiceAudioUploader.Models;

namespace NiceAudioUploader.Services;

public sealed class AudioProcessor(IAudioConverter converter, INiceAuthenticationService authentication,
    INiceFilesClient client, INicePathBuilder paths, ILogger<AudioProcessor> logger) : IAudioProcessor
{
    public async Task<BatchResult> ProcessAsync(AudioBatch batch, CancellationToken cancellationToken)
    {
        var results = new List<AudioProcessingResult>();
        if (batch.Files.Count == 0) return new(results.AsReadOnly(), false);
        NicePathBuilder.ValidateUniqueDestinations(batch, paths);
        try
        {
            await converter.EnsureAvailableAsync(cancellationToken);
            logger.LogInformation("Obtendo token NICE...");
            _ = await authentication.GetTokenAsync(cancellationToken);
            logger.LogInformation("Token obtido.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new(results.AsReadOnly(), true); }
        catch (Exception error)
        {
            var message = SafeErrors.Describe(error);
            logger.LogError("Não foi possível iniciar o lote: {Error}", message);
            return new(batch.Files.Select(f => new AudioProcessingResult(Path.GetFileName(f),
                paths.BuildRemotePath(batch.Environment, Path.GetFileName(f)), false, message)).ToArray(), false);
        }

        foreach (var file in batch.Files)
        {
            if (cancellationToken.IsCancellationRequested) return new(results.AsReadOnly(), true);
            var name = Path.GetFileName(file);
            var remote = paths.BuildRemotePath(batch.Environment, name);
            string? temporaryDirectory = null;
            try
            {
                // CreateTempSubdirectory uses a unique directory under the platform temp path.
                temporaryDirectory = Directory.CreateTempSubdirectory("nice-audio-").FullName;
                var converted = Path.Combine(temporaryDirectory, "converted.wav");
                logger.LogInformation("[{Index}/{Total}] {File}: convertendo...", results.Count + 1, batch.Files.Count, name);
                await converter.ConvertAsync(file, converted, cancellationToken);
                logger.LogInformation("Conversão concluída. Gerando Base64 do WAV completo...");
                var bytes = await File.ReadAllBytesAsync(converted, cancellationToken);
                if (bytes.Length == 0) throw new UserVisibleException("O arquivo convertido está vazio.");
                var base64 = Convert.ToBase64String(bytes);
                logger.LogInformation("Enviando para {Destination}...", remote);
                var response = await client.UploadAsync(new NiceFileUploadRequest
                {
                    FileName = remote, File = base64, Overwrite = batch.Overwrite
                }, cancellationToken);
                results.Add(new(name, remote, response.Success, response.Error));
                if (response.Success) logger.LogInformation("Upload concluído.");
                else logger.LogError("Falha no upload: {Error}", response.Error);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                results.Add(new(name, remote, false, "Cancelado. Se o envio já havia iniciado, verifique o destino na NICE."));
                return new(results.AsReadOnly(), true);
            }
            catch (Exception error)
            {
                var message = SafeErrors.Describe(error);
                results.Add(new(name, remote, false, message));
                logger.LogError("Falha: {Error}. Continuando processamento.", message);
            }
            finally
            {
                if (temporaryDirectory is not null)
                {
                    try { Directory.Delete(temporaryDirectory, recursive: true); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        logger.LogWarning("Não foi possível remover o temporário {Directory}. Remova-o manualmente.", temporaryDirectory);
                    }
                }
            }
        }
        return new(results.AsReadOnly(), cancellationToken.IsCancellationRequested);
    }
}
