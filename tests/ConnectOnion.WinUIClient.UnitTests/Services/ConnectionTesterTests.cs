using System.Net;
using System.Net.Http;
using ConnectOnion.WinUIClient.Services;

namespace ConnectOnion.WinUIClient.UnitTests.Services;

public sealed class ConnectionTesterTests
{
    [Theory]
    [InlineData("ws://agent.test/base", "http://agent.test/base/health")]
    [InlineData("wss://agent.test/base", "https://agent.test/base/health")]
    public async Task DirectUrl_UsesHealthEndpoint(string directUrl, string expectedUrl)
    {
        var handler = new StubHandler("""{"status":"ok","name":"test-agent"}""");
        var tester = new ConnectionTester(new HttpClient(handler));

        var result = await tester.TestAsync(null, directUrl);

        Assert.True(result.Ok);
        Assert.Equal("test-agent", result.AgentName);
        Assert.Contains("Test Agent", result.Detail);
        Assert.Equal(expectedUrl, handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task ExplicitUnhealthyResponse_FailsWithSpecificReason()
    {
        var tester = new ConnectionTester(new HttpClient(
            new StubHandler("""{"healthy":false}""")));

        var result = await tester.TestAsync(null, "wss://agent.test");

        Assert.False(result.Ok);
        Assert.Contains("unhealthy", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }
}
