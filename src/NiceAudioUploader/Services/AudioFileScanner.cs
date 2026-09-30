namespace NiceAudioUploader.Services;

public sealed class AudioFileScanner : IAudioFileScanner
{
    public static IReadOnlyList<string> SupportedExtensions { get; } =
        Array.AsReadOnly(new[] { ".wav", ".mp3", ".m4a", ".aac", ".flac", ".ogg" });

    public IReadOnlyList<string> Scan(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new UserVisibleException("Informe uma pasta.");
        try
        {
            // Enumeration distinguishes missing directories from permission errors.
            var files = Directory.EnumerateFiles(Path.GetFullPath(folder), "*", SearchOption.TopDirectoryOnly)
                .Where(p => SupportedExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
            if (files.Length == 0)
                throw new UserVisibleException("Nenhum arquivo de áudio válido foi encontrado. Selecione outra pasta.");
            return Array.AsReadOnly(files);
        }
        catch (DirectoryNotFoundException) { throw new UserVisibleException("A pasta selecionada não existe."); }
        catch (UnauthorizedAccessException) { throw new UserVisibleException("A pasta selecionada não pode ser acessada."); }
        catch (ArgumentException) { throw new UserVisibleException("O caminho da pasta é inválido."); }
    }
}
