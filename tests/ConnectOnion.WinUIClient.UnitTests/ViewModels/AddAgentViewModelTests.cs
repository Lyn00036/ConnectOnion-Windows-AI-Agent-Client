using System.Net;
using System.Net.Http;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.ViewModels;

namespace ConnectOnion.WinUIClient.UnitTests.ViewModels;

public sealed class AddAgentViewModelTests
{
    [Fact]
    public void EmptyInput_DoesNotShowValidationError()
    {
        var vm = CreateViewModel();
        vm.Reset();

        Assert.False(vm.IsInputValid);
        Assert.False(vm.ShowValidationError);
        Assert.Equal("", vm.InputHelpText);
        Assert.False(vm.CanTest);
        Assert.False(vm.CanAdd);
    }

    [Theory]
    [InlineData("not-an-agent")]
    [InlineData("https://example.com/agent")]
    [InlineData("ws://")]
    [InlineData("0x1234")]
    public void EditedInvalidInput_ShowsInlineValidation(string input)
    {
        var vm = CreateViewModel();

        vm.Input = input;

        Assert.False(vm.IsInputValid);
        Assert.True(vm.ShowValidationError);
        Assert.Equal(AddAgentViewModel.ValidationError, vm.InputHelpText);
        Assert.False(vm.CanTest);
    }

    [Theory]
    [InlineData("ws://example.com/agent")]
    [InlineData("wss://example.com/agent")]
    public void WebSocketUrl_IsValidAndEnablesTesting(string input)
    {
        var vm = CreateViewModel();

        vm.Input = input;

        Assert.True(vm.IsInputValid);
        Assert.False(vm.ShowValidationError);
        Assert.True(vm.CanTest);
        Assert.False(vm.CanAdd);
    }

    [Fact]
    public void AgentAddress_IsValidAndEnablesTesting()
    {
        var vm = CreateViewModel();

        vm.Input = "0x" + new string('a', 64);

        Assert.True(vm.IsInputValid);
        Assert.True(vm.CanTest);
    }

    private static AddAgentViewModel CreateViewModel()
        => new(
            new AgentRepository(),
            new ConnectionTester(new HttpClient(new NeverCalledHandler())));

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }
}
