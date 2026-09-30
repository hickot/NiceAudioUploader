using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NiceAudioUploader;
using NiceAudioUploader.Configuration;
using NiceAudioUploader.ConsoleUi;
using NiceAudioUploader.Services;

Console.OutputEncoding = Encoding.UTF8;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
    builder.Configuration.SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddUserSecrets<ConsoleApplication>(optional: true)
        .AddEnvironmentVariables();
    builder.Services.Configure<NiceOptions>(builder.Configuration.GetSection("Nice"));
    builder.Services.PostConfigure<NiceOptions>(options =>
    {
        options.AccessKeyId = Environment.GetEnvironmentVariable("NICE_ACCESS_KEY_ID") ?? options.AccessKeyId;
        options.AccessKeySecret = Environment.GetEnvironmentVariable("NICE_ACCESS_KEY_SECRET") ?? options.AccessKeySecret;
    });
    builder.Services.Configure<FfmpegOptions>(builder.Configuration.GetSection("Ffmpeg"));
    builder.Services.Configure<AudioProcessingOptions>(builder.Configuration.GetSection("AudioProcessing"));
    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(options => { options.SingleLine = true; options.TimestampFormat = "HH:mm:ss "; });
    builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
    builder.Services.AddHttpClient("Nice", (services, client) =>
    {
        client.Timeout = TimeSpan.FromSeconds(services.GetRequiredService<IOptions<NiceOptions>>().Value.TimeoutSeconds);
        client.MaxResponseContentBufferSize = 1024 * 1024;
    }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<HttpRetry>();
    builder.Services.AddSingleton<IFolderSelector, FolderSelector>();
    builder.Services.AddSingleton<IAudioFileScanner, AudioFileScanner>();
    builder.Services.AddSingleton<INicePathBuilder, NicePathBuilder>();
    builder.Services.AddSingleton<IAudioConverter, FfmpegAudioConverter>();
    builder.Services.AddSingleton<INiceAuthenticationService, NiceAuthenticationService>();
    builder.Services.AddSingleton<INiceFilesClient, NiceFilesClient>();
    builder.Services.AddSingleton<IAudioProcessor, AudioProcessor>();
    builder.Services.AddSingleton<ConsoleApplication>();
    using var host = builder.Build();
    host.Services.GetRequiredService<IOptions<NiceOptions>>().Value.Validate();
    return await host.Services.GetRequiredService<ConsoleApplication>().RunAsync(cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("Operação cancelada.");
    return 130;
}
catch (Exception error)
{
    Console.Error.WriteLine(SafeErrors.Describe(error));
    return 1;
}
