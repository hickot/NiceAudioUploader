using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NiceAudioUploader.Models;
using NiceAudioUploader.Services;

namespace NiceAudioUploader.Tests;

public sealed class HttpTests
{
    [Fact]
    public async Task AuthenticationUsesReferenceContractCachesAndRenewsToken()
    {
        var clock = new TestClock();
        var handler = new StubHandler(async (request, count, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/authentication/v1/token/access-key", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
            Assert.Equal("synthetic-id", body.RootElement.GetProperty("accessKeyId").GetString());
            Assert.Equal("synthetic-secret", body.RootElement.GetProperty("accessKeySecret").GetString());
            return StubHandler.Json($$"""{"access_token":"token-{{count}}","expires_in":"100"}""");
        });
        var options = TestOptions.Nice();
        using var auth = new NiceAuthenticationService(new StubFactory(handler), options, new HttpRetry(options, clock), clock);
        Assert.Equal("token-1", await auth.GetTokenAsync(default));
        clock.Advance(TimeSpan.FromSeconds(50));
        Assert.Equal("token-1", await auth.GetTokenAsync(default));
        clock.Advance(TimeSpan.FromSeconds(50));
        Assert.Equal("token-2", await auth.GetTokenAsync(default));
        Assert.Equal("token-3", await auth.GetTokenAsync(default, forceRefresh: true));
        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"access_token\":\"\"}")]
    [InlineData("not-json")]
    [InlineData("{\"access_token\":\"synthetic\",\"expires_in\":-1}")]
    [InlineData("{\"access_token\":\"synthetic\",\"expires_in\":\"bad\"}")]
    public async Task RejectsUnexpectedAuthenticationJsonWithoutLeakingBody(string body)
    {
        var handler = new StubHandler((_, _, _) => Task.FromResult(StubHandler.Json(body)));
        var options = TestOptions.Nice();
        using var auth = new NiceAuthenticationService(new StubFactory(handler), options, new HttpRetry(options, TimeProvider.System), TimeProvider.System);
        var error = await Assert.ThrowsAsync<UserVisibleException>(() => auth.GetTokenAsync(default));
        Assert.DoesNotContain("synthetic", error.Message);
    }

    [Fact]
    public async Task MissingExpiryReusesTokenAndMissingCredentialsMakeNoRequest()
    {
        var handler = new StubHandler((_, _, _) => Task.FromResult(StubHandler.Json("{\"access_token\":\"test\"}")));
        var options = TestOptions.Nice();
        using var auth = new NiceAuthenticationService(new StubFactory(handler), options, new HttpRetry(options, TimeProvider.System), TimeProvider.System);
        await auth.GetTokenAsync(default);
        await auth.GetTokenAsync(default);
        Assert.Equal(1, handler.Calls);
        options.Value.AccessKeySecret = "";
        await Assert.ThrowsAsync<UserVisibleException>(() => auth.GetTokenAsync(default, true));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task UploadUsesExactPayloadAndRefreshesOnceOn401()
    {
        var authenticationCalls = 0;
        var uploadCalls = 0;
        var handler = new StubHandler(async (request, _, ct) =>
        {
            if (request.RequestUri!.Host == "auth.example.test")
            {
                authenticationCalls++;
                return StubHandler.Json($$"""{"access_token":"token-{{authenticationCalls}}"}""");
            }
            uploadCalls++;
            Assert.Equal("/incontactapi/services/v33.0/files", request.RequestUri.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal($"token-{authenticationCalls}", request.Headers.Authorization.Parameter);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.Contains(request.Headers.Accept, a => a.MediaType == "application/json");
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(3, json.RootElement.EnumerateObject().Count());
            Assert.Equal("Nice/PVI/Prompts/DEV/test.wav", json.RootElement.GetProperty("fileName").GetString());
            Assert.Equal("UklGRg==", json.RootElement.GetProperty("file").GetString());
            Assert.True(json.RootElement.GetProperty("overwrite").GetBoolean());
            return StubHandler.Json("{}", uploadCalls == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
        });
        var options = TestOptions.Nice();
        var factory = new StubFactory(handler);
        var retry = new HttpRetry(options, TimeProvider.System);
        using var auth = new NiceAuthenticationService(factory, options, retry, TimeProvider.System);
        var client = new NiceFilesClient(factory, auth, options, retry);
        Assert.True((await client.UploadAsync(Request(), default)).Success);
        Assert.True((await client.UploadAsync(Request(), default)).Success);
        Assert.Equal(2, authenticationCalls);
        Assert.Equal(3, uploadCalls);
    }

    [Theory]
    [InlineData(400, 1, 0)]
    [InlineData(401, 2, 1)]
    [InlineData(403, 1, 0)]
    [InlineData(404, 1, 0)]
    [InlineData(429, 3, 0)]
    [InlineData(500, 3, 0)]
    public async Task HttpErrorsHaveBoundedRetries(int status, int expectedCalls, int expectedRefreshes)
    {
        var handler = new StubHandler((_, _, _) =>
        {
            var response = StubHandler.Json("{\"secret\":\"never-log-this\"}", (HttpStatusCode)status);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        });
        var auth = new FakeAuthentication();
        var client = Client(handler, auth, 2);
        var result = await client.UploadAsync(Request(), default);
        Assert.False(result.Success);
        Assert.Equal(status, result.StatusCode);
        Assert.DoesNotContain("never-log-this", result.Error);
        Assert.Equal(expectedCalls, handler.Calls);
        Assert.Equal(expectedRefreshes, auth.Refreshes);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 3600)]
    public async Task DoesNotRetryWithoutOverwriteOrBeforeLongRetryAfter(bool overwrite, int delay)
    {
        var handler = new StubHandler((_, _, _) =>
        {
            var response = StubHandler.Json("{}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(delay));
            return Task.FromResult(response);
        });
        Assert.False((await Client(handler, new FakeAuthentication(), 2).UploadAsync(Request(overwrite), default)).Success);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("<html>unexpected</html>")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task UnexpectedSuccessBodyIsReported(string body)
    {
        var handler = new StubHandler((_, _, _) => Task.FromResult(StubHandler.Json(body)));
        Assert.False((await Client(handler, new FakeAuthentication()).UploadAsync(Request(), default)).Success);
    }

    [Fact]
    public async Task TimeoutAndConnectionErrorsAreNotRetriedAndCancellationPropagates()
    {
        foreach (var error in new Exception[] { new TaskCanceledException(), new HttpRequestException("sensitive detail") })
        {
            var handler = new StubHandler((_, _, _) => throw error);
            var result = await Client(handler, new FakeAuthentication(), 2).UploadAsync(Request(), default);
            Assert.False(result.Success);
            Assert.DoesNotContain("sensitive detail", result.Error);
            Assert.Equal(1, handler.Calls);
        }
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelling = new StubHandler((_, _, ct) => Task.FromCanceled<HttpResponseMessage>(ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(cancelling, new FakeAuthentication()).UploadAsync(Request(), cts.Token));
    }

    [Fact]
    public async Task AuthenticationFailureAndTransientRecoveryAreHandled()
    {
        var options = TestOptions.Nice(2);
        var handler = new StubHandler((_, count, _) =>
        {
            var response = count == 1 ? StubHandler.Json("{}", HttpStatusCode.ServiceUnavailable) : StubHandler.Json("{\"access_token\":\"ok\"}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        });
        using var auth = new NiceAuthenticationService(new StubFactory(handler), options, new HttpRetry(options, TimeProvider.System), TimeProvider.System);
        Assert.Equal("ok", await auth.GetTokenAsync(default));
        Assert.Equal(2, handler.Calls);
        var denied = new StubHandler((_, _, _) => Task.FromResult(StubHandler.Json("sensitive", HttpStatusCode.Unauthorized)));
        using var deniedAuth = new NiceAuthenticationService(new StubFactory(denied), options, new HttpRetry(options, TimeProvider.System), TimeProvider.System);
        var error = await Assert.ThrowsAsync<UserVisibleException>(() => deniedAuth.GetTokenAsync(default));
        Assert.Contains("401", error.Message);
        Assert.DoesNotContain("sensitive", error.Message);
        Assert.Equal(1, denied.Calls);
    }

    private static NiceFileUploadRequest Request(bool overwrite = true) => new()
    {
        FileName = "Nice/PVI/Prompts/DEV/test.wav", File = "UklGRg==", Overwrite = overwrite
    };
    private static NiceFilesClient Client(StubHandler handler, FakeAuthentication auth, int retries = 0)
    {
        var options = TestOptions.Nice(retries);
        return new(new StubFactory(handler), auth, options, new HttpRetry(options, TimeProvider.System));
    }
}

internal sealed class FakeAuthentication : INiceAuthenticationService
{
    public int Calls { get; private set; }
    public int Refreshes { get; private set; }
    public Task<string> GetTokenAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        Calls++;
        if (forceRefresh) Refreshes++;
        return Task.FromResult("synthetic-token");
    }
}
