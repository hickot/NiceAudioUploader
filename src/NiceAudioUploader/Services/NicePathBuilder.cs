using NiceAudioUploader.Models;

namespace NiceAudioUploader.Services;

public sealed class NicePathBuilder : INicePathBuilder
{
    public string BuildRemotePath(NiceEnvironment environment, string audioFileName)
    {
        if (!Enum.IsDefined(environment)) throw new ArgumentOutOfRangeException(nameof(environment));
        if (string.IsNullOrWhiteSpace(audioFileName) || audioFileName.IndexOfAny(['/', '\\']) >= 0 ||
            audioFileName.Any(char.IsControl) || audioFileName.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0 ||
            string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(audioFileName)) ||
            Path.GetFileNameWithoutExtension(audioFileName) is "." or "..")
            throw new UserVisibleException("Nome de áudio inválido para um destino multiplataforma.");
        return $"Nice/PVI/Prompts/{environment}/{Path.GetFileNameWithoutExtension(audioFileName)}.wav";
    }

    public static void ValidateUniqueDestinations(AudioBatch batch, INicePathBuilder builder)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in batch.Files)
            if (!paths.Add(builder.BuildRemotePath(batch.Environment, Path.GetFileName(file)).Normalize()))
                throw new UserVisibleException("Existem áudios com o mesmo nome final .wav. Renomeie-os antes do envio para evitar sobrescrita dentro do lote.");
    }
}
