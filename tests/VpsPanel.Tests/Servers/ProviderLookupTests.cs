using System.Net;
using VpsPanel.Servers;

namespace VpsPanel.Tests.Servers;

public sealed class ProviderLookupTests
{
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Theory(DisplayName = "org field with an AS number => hosting name without the number")]
    [InlineData("AS12345 Example Hosting", "Example Hosting")]
    [InlineData("AS1 Example, Inc.", "Example, Inc.")]
    [InlineData("Example Hosting", "Example Hosting")]
    public void FromOrg_Should_StripAsNumber_When_Present(string org, string expected)
    {
        // Arrange
        var value = org;

        // Act
        var name = ProviderName.FromOrg(value);

        // Assert
        name.Should().Be(expected);
    }

    [Fact(DisplayName = "ipinfo returned org => hosting name, request by IP")]
    public async Task Find_Should_ReturnProvider_When_OrgPresent()
    {
        // Arrange
        var handler = new StubHandler("""{"ip":"192.0.2.10","org":"AS64500 Example Hosting"}""");
        var lookup = new IpInfoProviderLookup(new HttpClient(handler));

        // Act
        var provider = await lookup.Find("192.0.2.10", CancellationToken.None);

        // Assert
        provider.Should().Be("Example Hosting");
        handler.RequestUri.Should().Be(new Uri("https://ipinfo.io/192.0.2.10/json"));
    }

    [Fact(DisplayName = "ipinfo returned bogon without org => no hosting")]
    public async Task Find_Should_ReturnNull_When_AddressBogon()
    {
        // Arrange
        var lookup = new IpInfoProviderLookup(new HttpClient(new StubHandler("""{"ip":"192.0.2.10","bogon":true}""")));

        // Act
        var provider = await lookup.Find("192.0.2.10", CancellationToken.None);

        // Assert
        provider.Should().BeNull();
    }
}