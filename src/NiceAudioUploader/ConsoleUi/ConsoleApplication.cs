using Microsoft.Extensions.Options;
using NiceAudioUploader.Configuration;
using NiceAudioUploader.Models;
using NiceAudioUploader.Services;

namespace NiceAudioUploader.ConsoleUi;

public sealed class ConsoleApplication(IFolderSelector folderSelector, IAudioFileScanner scanner,
    INicePathBuilder paths, IAudioProcessor processor, IOptions<AudioProcessingOptions> options)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("========================================\n          NICE AUDIO UPLOADER\n========================================");
        NiceEnvironment environment;
        while (true)
        {
            Console.WriteLine("\nSelecione o ambiente:\n[1] DEV — Desenvolvimento\n[2] HML — Homologação\n(vazio cancela)");
            var answer = (await ConsoleInput.ReadAsync(cancellationToken))?.Trim();
            if (string.IsNullOrEmpty(answer) || cancellationToken.IsCancellationRequested) return 130;
            if (answer is "1" or "2") { environment = answer == "1" ? NiceEnvironment.DEV : NiceEnvironment.HML; break; }
            Console.WriteLine("Ambiente inválido. Escolha 1 ou 2.");
        }

        AudioBatch batch;
        while (true)
        {
            var folder = await folderSelector.SelectFolderAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(folder) || cancellationToken.IsCancellationRequested) return 130;
            try
            {
                batch = new AudioBatch(environment, folder, scanner.Scan(folder), options.Value.Overwrite);
                NicePathBuilder.ValidateUniqueDestinations(batch, paths);
                break;
            }
            catch (Exception error) when (error is UserVisibleException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                Console.WriteLine(SafeErrors.Describe(error));
            }
        }

        Console.WriteLine($"\nRESUMO DO ENVIO\nAmbiente NICE: {batch.Environment}\nPasta: {batch.Folder}\nDestino NICE: Nice/PVI/Prompts/{batch.Environment}/\nArquivos encontrados: {batch.Files.Count}");
        for (var i = 0; i < batch.Files.Count; i++) Console.WriteLine($"{i + 1}. {Path.GetFileName(batch.Files[i])}");
        Console.WriteLine($"\nConversão: WAV / 8000 Hz / Mono / PCM μ-law\nOverwrite: {(batch.Overwrite ? "SIM" : "NÃO")}");
        while (true)
        {
            Console.WriteLine("\nDeseja iniciar o envio? [S] Sim / [N] Não");
            var answer = (await ConsoleInput.ReadAsync(cancellationToken))?.Trim();
            if (string.IsNullOrEmpty(answer) || answer.Equals("N", StringComparison.OrdinalIgnoreCase) || cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("Envio cancelado.");
                return 130;
            }
            if (answer.Equals("S", StringComparison.OrdinalIgnoreCase)) break;
            Console.WriteLine("Resposta inválida. Digite S ou N.");
        }

        var result = await processor.ProcessAsync(batch, cancellationToken);
        Console.WriteLine($"\n========================================\nPROCESSAMENTO {(result.Cancelled ? "CANCELADO" : "CONCLUÍDO")}\n========================================\nAmbiente: {batch.Environment}\nPasta processada: {batch.Folder}\nDestino: Nice/PVI/Prompts/{batch.Environment}/\nArquivos encontrados: {batch.Files.Count}\nUploads realizados: {result.Files.Count(f => f.Success)}\nFalhas: {result.Files.Count(f => !f.Success)}\nNão processados: {batch.Files.Count - result.Files.Count}");
        foreach (var file in result.Files.Where(f => !f.Success)) Console.WriteLine($"\n{file.FileName}\nErro: {file.Error}");
        return result.Cancelled ? 130 : result.Files.Any(f => !f.Success) ? 1 : 0;
    }
}
