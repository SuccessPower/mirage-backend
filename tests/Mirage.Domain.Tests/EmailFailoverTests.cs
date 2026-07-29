using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Infrastructure.Email;
using Xunit;

namespace Mirage.Domain.Tests;

public sealed class EmailFailoverTests
{
    [Fact]
    public async Task Send_UsesConfiguredOrder_AndStopsAfterFirstSuccess()
    {
        var zeptoMail = new StubTransport("ZeptoMail", configured: true, succeeds: false);
        var ses = new StubTransport("AmazonSes", configured: true, succeeds: true);
        var mailjet = new StubTransport("Mailjet", configured: true, succeeds: true);
        var service = CreateService([mailjet, ses, zeptoMail]);

        var sent = await service.SendWelcomeEmailAsync("person@example.com", "Person");

        Assert.True(sent);
        Assert.Equal(1, zeptoMail.SendCount);
        Assert.Equal(1, ses.SendCount);
        Assert.Equal(0, mailjet.SendCount);
    }

    [Fact]
    public async Task Send_SkipsUnconfiguredProviders()
    {
        var zeptoMail = new StubTransport("ZeptoMail", configured: false, succeeds: true);
        var ses = new StubTransport("AmazonSes", configured: true, succeeds: true);
        var mailjet = new StubTransport("Mailjet", configured: true, succeeds: true);
        var service = CreateService([zeptoMail, ses, mailjet]);

        var sent = await service.SendWelcomeEmailAsync("person@example.com", "Person");

        Assert.True(sent);
        Assert.Equal(0, zeptoMail.SendCount);
        Assert.Equal(1, ses.SendCount);
        Assert.Equal(0, mailjet.SendCount);
    }

    [Fact]
    public async Task Send_ReturnsFalse_WhenEveryConfiguredProviderFails()
    {
        var transports = new[]
        {
            new StubTransport("ZeptoMail", configured: true, succeeds: false),
            new StubTransport("AmazonSes", configured: true, succeeds: false),
            new StubTransport("Mailjet", configured: true, succeeds: false)
        };
        var service = CreateService(transports);

        var sent = await service.SendWelcomeEmailAsync("person@example.com", "Person");

        Assert.False(sent);
        Assert.All(transports, transport => Assert.Equal(1, transport.SendCount));
    }

    private static ResilientEmailService CreateService(IEnumerable<IEmailTransport> transports)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:ProviderOrder:0"] = "ZeptoMail",
                ["Email:ProviderOrder:1"] = "AmazonSes",
                ["Email:ProviderOrder:2"] = "Mailjet"
            })
            .Build();
        return new ResilientEmailService(
            transports,
            configuration,
            NullLogger<ResilientEmailService>.Instance);
    }

    private sealed class StubTransport(string name, bool configured, bool succeeds) : IEmailTransport
    {
        public string Name { get; } = name;
        public bool IsConfigured { get; } = configured;
        public int SendCount { get; private set; }

        public Task<bool> SendAsync(EmailTransportMessage message, CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(succeeds);
        }
    }
}
