# NICE Audio Uploader

Aplicação Console em C#/.NET 8 para Windows e macOS. Seleciona DEV ou HML e uma pasta local, apresenta os arquivos e solicita confirmação antes de converter ou enviar. Cada áudio é convertido para **WAV / 8000 Hz / mono / PCM μ-law**, codificado integralmente em Base64 e enviado à NICE CXone.

## Executar

Na pasta desta solution:

```text
dotnet restore
dotnet build
dotnet test
dotnet run --project src/NiceAudioUploader
```

Também é possível entrar em `src/NiceAudioUploader` e executar `dotnet run`.

Pré-requisitos: SDK .NET 8 (8.0.403 ou feature band posterior do SDK 8, conforme `global.json`), FFmpeg no PATH ou configurado por caminho absoluto e credenciais Access Key com permissão para gravar arquivos na NICE. Node.js e ffprobe são necessários apenas para os testes opcionais de equivalência. Instale as atualizações de manutenção do SDK/runtime .NET 8 disponíveis para sua plataforma.

## FFmpeg

macOS, com Homebrew instalado:

```sh
brew install ffmpeg
ffmpeg -version
ffprobe -version
```

Windows: obtenha uma distribuição compilada indicada na [página de downloads do FFmpeg](https://ffmpeg.org/download.html), extraia e adicione a pasta `bin` ao PATH do usuário. Abra um novo terminal e execute `ffmpeg -version`. Alternativamente, configure o caminho completo para `ffmpeg.exe`.

No `src/NiceAudioUploader/appsettings.json`:

```json
{
  "Ffmpeg": {
    "ExecutablePath": "ffmpeg",
    "TimeoutSeconds": 300
  }
}
```

Exemplos de `ExecutablePath`: `C:\\Ferramentas\\ffmpeg\\bin\\ffmpeg.exe` em JSON no Windows, `/opt/homebrew/bin/ffmpeg` em macOS Apple Silicon. Esses caminhos são exemplos; o padrão é resolver `ffmpeg` no PATH. Não inclua aspas adicionais no valor. O programa usa `ProcessStartInfo.ArgumentList` sem shell, scripts ou comandos específicos de sistema operacional. FFmpeg é uma dependência externa e não é incluído nos builds publicados.

## Configuração NICE e credenciais

Os valores padrão vêm dos arquivos de referência fornecidos:

| Configuração | Valor |
| --- | --- |
| `Nice:AuthenticationBaseUrl` | `https://na1.nice-incontact.com` |
| `Nice:AuthenticationPath` | `/authentication/v1/token/access-key` |
| `Nice:ApiBaseUrl` | `https://api-c37.nice-incontact.com` |
| `Nice:FilesPath` | `/incontactapi/services/v33.0/files` |
| `Nice:TimeoutSeconds` | `120`, por tentativa HTTP |
| `Nice:MaxRetries` | `2`, além da tentativa inicial |
| `Nice:MaxRetryDelaySeconds` | `60` |
| `AudioProcessing:Overwrite` | `true` |

Configure as URLs no `appsettings.json`; os hosts devem usar HTTPS, sem credenciais embutidas, paths ou query strings. Os paths ficam em suas próprias opções. O JSON é carregado da pasta do executável e copiado automaticamente no build/publish.

**Não coloque credenciais em `appsettings.json`.** Defina `NICE_ACCESS_KEY_ID` e `NICE_ACCESS_KEY_SECRET` no ambiente do processo. Exemplo de sintaxe (substitua os placeholders de forma segura em seu ambiente):

macOS, zsh:

```sh
export NICE_ACCESS_KEY_ID='<ACCESS_KEY_ID>'
export NICE_ACCESS_KEY_SECRET='<ACCESS_KEY_SECRET>'
dotnet run --project src/NiceAudioUploader
```

Windows, PowerShell (apenas uma opção para configurar o terminal, não uma dependência da aplicação):

```powershell
$env:NICE_ACCESS_KEY_ID = '<ACCESS_KEY_ID>'
$env:NICE_ACCESS_KEY_SECRET = '<ACCESS_KEY_SECRET>'
dotnet run --project src/NiceAudioUploader
```

Evite inserir valores reais em comandos que ficam no histórico. Para desenvolvimento, o projeto também aceita .NET User Secrets:

```text
dotnet user-secrets set "Nice:AccessKeyId" "<ACCESS_KEY_ID>" --project src/NiceAudioUploader
dotnet user-secrets set "Nice:AccessKeySecret" "<ACCESS_KEY_SECRET>" --project src/NiceAudioUploader
```

User Secrets fica fora do repositório, mas não é armazenamento criptografado. Em distribuição, prefira injeção das variáveis por um mecanismo seguro da organização. Os exemplos de cURL originais não foram copiados para o projeto.

Precedência: `appsettings.json` → User Secrets → variáveis de ambiente; `NICE_ACCESS_KEY_ID`/`NICE_ACCESS_KEY_SECRET` prevalecem sobre `Nice__AccessKeyId`/`Nice__AccessKeySecret`. Qualquer opção pode ser sobrescrita por variável com `__`, por exemplo `Ffmpeg__ExecutablePath` ou `Nice__ApiBaseUrl`. Não há pasta nem ambiente de destino fixos na configuração.

## Operação

1. Escolha `1` para DEV ou `2` para HML. Nenhum outro ambiente é aceito.
2. Digite o caminho completo da pasta, com ou sem aspas duplas. Não use `~` ou escapes de shell; cole o caminho literal.
3. Confira a lista, o ambiente, o destino e `Overwrite: SIM`.
4. Digite `S` para iniciar; `N` ou entrada vazia cancela.
5. Acompanhe os logs e o resumo com sucessos, falhas e arquivos não processados.

Exemplos de pastas: `/Users/usuario/Documents/Prompts` ou `C:\Users\Usuario\Documents\Prompts`.

São encontrados `.wav`, `.mp3`, `.m4a`, `.aac`, `.flac` e `.ogg`, independentemente de maiúsculas/minúsculas, **apenas diretamente na pasta**. A extensão seleciona candidatos; conteúdo corrompido ou codec não disponível na instalação FFmpeg é registrado como falha individual. Subpastas e extensões não suportadas são ignoradas.

O nome base é preservado, com extensão `.wav`:

```text
DEV + menu principal.mp3 → Nice/PVI/Prompts/DEV/menu principal.wav
HML + saudação.wav       → Nice/PVI/Prompts/HML/saudação.wav
```

Caminhos remotos sempre usam `/`. Ambiente, lista e overwrite são capturados em um lote imutável antes da confirmação. A seleção DEV/HML muda **o caminho remoto**; os exemplos fornecidos usam os mesmos hosts/credenciais, não demonstram contas separadas por ambiente.

Colisões de nome final, inclusive variações de caixa ou normalização Unicode, bloqueiam o lote antes de autenticar. Por exemplo, renomeie `menu.mp3` ou `menu.wav` se ambos estiverem na pasta. Isso evita que o próprio lote sobrescreva um áudio recém-enviado. A comparação conservadora não pressupõe regras de caixa da NICE.

Os originais são abertos somente para leitura. A conversão usa diretórios temporários exclusivos do sistema operacional. Os temporários são removidos em `finally`, inclusive após falha/cancelamento; se o sistema impedir a limpeza, o log informa o diretório. `Ctrl+C` cancela requests, encerra o processo FFmpeg e apresenta o resultado parcial quando o lote já começou. Encerramento forçado do processo ou desligamento do computador pode deixar temporários.

Códigos de saída: `0` = lote concluído sem falhas; `1` = falha de configuração/processamento; `130` = cancelamento.

## Integração e decisões

Os três arquivos foram analisados integralmente antes da implementação. `Encode.js` usa `-ar 8000 -ac 1 -acodec pcm_mulaw`, lê todo o WAV e gera Base64. A implementação reproduz o formato e adiciona execução sem shell, timeout, cancelamento, validação do container e temporários únicos. Usa `-n` para não sobrescrever arquivos locais existentes e descarta vídeo com `-vn`. Não cria TXT com Base64.

`Token_Nice.txt` define POST JSON com `accessKeyId`/`accessKeySecret`. Como não contém resposta, o DTO usa `access_token` e `expires_in` documentados no [guia oficial UserHub da NICE](https://developer.niceincontact.com/Documentation/UserHubGettingStarted). `expires_in` aceita número ou string numérica. O token fica somente em memória e é renovado pouco antes da expiração. Se a expiração não vier, é reutilizado até um 401. Não há parsing de JWT nem uso de refresh token; a renovação repete a autenticação Access Key.

`Subida_Audio.txt` define POST, `Authorization: Bearer`, `Accept: application/json` e payload `fileName`, `file`, `overwrite`. Cookies do exemplo não são enviados. Redirecionamentos automáticos e armazenamento de cookies estão desativados. Headers sensíveis, corpos HTTP, stdout/stderr do FFmpeg e Base64 não são escritos nos logs. Diagnósticos de processo são capturados de forma limitada e erros exibidos usam mensagens próprias.

**Pendência de contrato:** o exemplo de upload não fornece schema do corpo da resposta. A aplicação considera concluído um status HTTP 2xx, exceto 202, com corpo vazio ou objeto JSON válido. Não inventa campos de sucesso de negócio. HTTP 202 indica conclusão não confirmada e é reportado para conferência; JSON inválido ou de outro tipo também é reportado. Se o tenant devolver erros de negócio dentro de HTTP 200, obtenha uma amostra sanitizada/documentação desse contrato e adapte `NiceFilesClient` antes de depender dessa semântica.

O primeiro 401 de um upload renova o token uma vez; um segundo 401 falha o arquivo. HTTP 429/5xx permite até dois retries com backoff de 1 e 2 segundos. `Retry-After`, tanto em segundos quanto em data HTTP, prevalece. Se exceder o limite configurado, o arquivo falha sem antecipar a tentativa. Upload só tem retries transitórios com `Overwrite=true`, sempre no mesmo caminho e com o mesmo conteúdo. Com overwrite desabilitado, esses retries não ocorrem. Timeout/DNS/conexão não têm retry automático porque o resultado de uma tentativa de envio pode ser indeterminado.

Falha de preflight (FFmpeg/autenticação) impede todo o lote e aparece no resumo. Falha individual de leitura, conversão ou upload permite continuar com os outros arquivos. Não há rollback de uploads já concluídos.

O WAV inteiro e seu Base64 ficam em memória por arquivo, como solicitado. O lote é sequencial; esta versão não impõe um limite arbitrário de tamanho da API. Para áudios muito grandes, considere a memória disponível e confirme os limites da conta NICE.

## Arquitetura

```text
src/NiceAudioUploader/
  Program.cs                 composição de DI/configuração/HTTP/logs
  ConsoleUi/                 prompts, seleção da pasta e resumos
  Configuration/             opções de NICE, FFmpeg e processamento
  Models/                    ambientes, DTOs, lote e resultados
  Services/
    AudioFileScanner         descoberta sem recursão
    NicePathBuilder          destino e validação de colisões
    FfmpegAudioConverter     processo multiplataforma e cancelamento
    WavValidator             RIFF/WAVE, μ-law, taxa, canais e dados
    NiceAuthenticationService cache em memória e renovação
    HttpRetry                política HTTP limitada
    NiceFilesClient          payload, autorização e resultado HTTP
    AudioProcessor           orquestração, Base64 e limpeza
tests/NiceAudioUploader.Tests/ testes xUnit e HTTP simulado
```

Os serviços têm interfaces e não dependem de prompts. Uma futura GUI pode reutilizar scanner, conversor, clientes e processador, fornecendo `AudioBatch` após confirmação do usuário. Configuração, logging e HTTP usam Microsoft.Extensions e `IHttpClientFactory`; JSON usa System.Text.Json.

## Testes

```text
dotnet test
```

Os testes unitários não acessam a NICE e não precisam de credenciais, FFmpeg ou Node.js. Cobrem destinos/nomes, colisões, snapshot do lote, pastas/extensões, cache/expiração, renovação 401, HTTP 400/403/404/429/500, erros de rede/timeout, respostas inesperadas, Base64 integral, continuidade e limpeza. O teste de pasta inacessível usa permissões Unix quando disponível; o comportamento de ACL Windows depende de validação no host.

Os testes opcionais de conversão são explicitamente ignorados sem `NICE_TEST_FFMPEG=1`. Para executá-los e comparar com o arquivo `Encode.js` original usando o **mesmo WAV sintético**:

macOS:

```sh
NICE_TEST_FFMPEG=1 NICE_ENCODE_JS='/caminho/Encode.js' dotnet test
```

Windows:

```powershell
$env:NICE_TEST_FFMPEG = '1'
$env:NICE_ENCODE_JS = 'C:\Referencias\Encode.js'
dotnet test
```

Requer `ffmpeg`, `ffprobe` e `node` no PATH. `NICE_ENCODE_JS` deve apontar para o script confiável fornecido. Ele é executado em diretório temporário contendo somente áudio sintético, removido após o teste. Nenhuma gravação real ou chamada NICE participa dos testes.

Os testes verificam WAV/8000 Hz/mono/pcm_mulaw via ffprobe e pelo validador RIFF, comparam as amostras decodificadas entre JavaScript e .NET e verificam hashes dos originais. Não exigem igualdade de metadados do container. Os seis formatos de entrada são gerados e convertidos durante o teste de integração.

## Publicação

Na raiz da solution, gere uma distribuição para cada sistema/arquitetura. Exemplos self-contained:

```text
dotnet publish src/NiceAudioUploader -c Release -r win-x64 --self-contained true -o artifacts/win-x64
dotnet publish src/NiceAudioUploader -c Release -r win-arm64 --self-contained true -o artifacts/win-arm64
dotnet publish src/NiceAudioUploader -c Release -r osx-arm64 --self-contained true -o artifacts/osx-arm64
dotnet publish src/NiceAudioUploader -c Release -r osx-x64 --self-contained true -o artifacts/osx-x64
```

Windows x64 normalmente usa `win-x64`; Mac Apple Silicon usa `osx-arm64`; Mac Intel usa `osx-x64`. A distribuição deve incluir toda a pasta publicada, inclusive `appsettings.json`. Execute `NiceAudioUploader.exe` em um terminal Windows ou `./NiceAudioUploader` em um terminal macOS. A versão self-contained inclui o runtime .NET, mas ainda requer FFmpeg para a plataforma de destino e a configuração segura das credenciais.

Para depender de um runtime .NET 8 instalado, publique com `--self-contained false`. O código é multiplataforma; o executável de um RID não deve ser usado em outro sistema. Builds Windows precisam de teste de execução em Windows; a validação local deste projeto ocorreu em macOS Apple Silicon.

## Troubleshooting

| Sintoma | Ação |
| --- | --- |
| Pasta inexistente/inacessível | Cole o caminho absoluto e confira permissões; uma nova seleção é solicitada. |
| Nenhum áudio encontrado | Confira as extensões e se os arquivos estão diretamente na pasta. |
| Nomes finais repetidos | Renomeie os arquivos que resultariam no mesmo `.wav`. |
| FFmpeg não encontrado | Verifique `ffmpeg -version`, PATH ou `Ffmpeg:ExecutablePath`. |
| Falha de conversão / ExitCode | Verifique se o áudio abre, se o codec existe no FFmpeg instalado e se há espaço temporário. |
| Credenciais ausentes | Defina as duas variáveis ou User Secrets no usuário que executa a aplicação. |
| HTTP 400 | Confira formato/nome e limites específicos do tenant. |
| HTTP 401 | Confira Access Key; após uma renovação o erro é reportado sem loop. |
| HTTP 403 | Confira permissões NICE para os arquivos/pasta. |
| HTTP 404 | Confira cluster, versão/path da API e destino. |
| HTTP 429 | Aguarde o intervalo exigido pela NICE antes de repetir. |
| HTTP 5xx | Verifique disponibilidade da NICE; retries são limitados. |
| DNS / TLS / conexão | Verifique host, rede, proxy e certificados. Nenhuma validação TLS é desabilitada. |
| Timeout / resposta inesperada | Confira se o arquivo chegou à NICE antes de reenviar. Ajuste timeout somente se necessário. |
| Temporário não removido | Feche processos que bloqueiam o diretório informado e remova-o manualmente. |

A autenticação e o upload reais dependem de acesso à rede/tenant e não foram executados durante o desenvolvimento. A primeira validação no ambiente NICE deve usar um prompt de teste e a confirmação normal do console.
