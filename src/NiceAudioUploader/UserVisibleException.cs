namespace NiceAudioUploader;

// Only messages authored by the application may reach the console; remote bodies and
// exception details can contain credentials, tokens or the submitted Base64.
public sealed class UserVisibleException(string message) : Exception(message);

public static class SafeErrors
{
    public static string Describe(Exception error) => error switch
    {
        UserVisibleException => error.Message,
        UnauthorizedAccessException => "Acesso negado ao arquivo ou à pasta.",
        FileNotFoundException => "Arquivo não encontrado.",
        DirectoryNotFoundException => "A pasta selecionada não existe.",
        IOException => "Erro de leitura/escrita: verifique acesso, bloqueio do arquivo e espaço em disco.",
        HttpRequestException => "Falha de DNS, conexão ou TLS. O resultado de um envio pode ser indeterminado.",
        OperationCanceledException => "Timeout da operação. O resultado de um envio pode ser indeterminado.",
        System.Text.Json.JsonException => "A NICE retornou JSON inesperado.",
        _ => "Falha inesperada durante o processamento. Revise a configuração e o arquivo."
    };
}
