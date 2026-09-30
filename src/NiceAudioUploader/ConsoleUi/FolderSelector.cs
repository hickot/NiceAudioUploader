using NiceAudioUploader.Services;

namespace NiceAudioUploader.ConsoleUi;

public sealed class FolderSelector : IFolderSelector
{
    public async Task<string?> SelectFolderAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("\nInforme a pasta contendo os áudios (caminho completo; vazio cancela):");
        return (await ConsoleInput.ReadAsync(cancellationToken))?.Trim().Trim('"');
    }
}

internal static class ConsoleInput
{
    public static Task<string?> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(Console.ReadLine, CancellationToken.None).WaitAsync(cancellationToken);
}
