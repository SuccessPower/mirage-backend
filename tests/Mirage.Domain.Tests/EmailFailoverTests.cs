using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Domain.Enums;
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

    [Fact]
    public async Task Send_AddsResponsiveDarkModeMarkup_AndConfiguredSocialLinks()
    {
        var transport = new StubTransport("ZeptoMail", configured: true, succeeds: true);
        var service = CreateService([
            transport,
            new StubTransport("AmazonSes", configured: false, succeeds: false),
            new StubTransport("Mailjet", configured: false, succeeds: false)
        ]);

        await service.SendWelcomeEmailAsync("person@example.com", "Person");

        Assert.NotNull(transport.LastMessage);
        Assert.Contains("prefers-color-scheme: dark", transport.LastMessage.Html);
        // The shared footer: brand lockup, one round badge per configured network, and the support mailbox.
        Assert.Contains("MIRAGE", transport.LastMessage.Html);
        Assert.Contains("https://www.instagram.com/themiragehub", transport.LastMessage.Html);
        Assert.Contains("mailto:support@themiragehub.com", transport.LastMessage.Html);
        // Only Instagram is configured in this fixture, so exactly two badges: that network and the mailbox.
        Assert.Equal(2, transport.LastMessage.Html.Split("border-radius:17px").Length - 1);
        Assert.DoesNotContain("facebook.com", transport.LastMessage.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Send_UsesConfiguredFrontendDomainForOpenMirageFooter()
    {
        var transport = new StubTransport("ZeptoMail", configured: true, succeeds: true);
        var service = CreateService([
            transport,
            new StubTransport("AmazonSes", configured: false, succeeds: false),
            new StubTransport("Mailjet", configured: false, succeeds: false)
        ], new Dictionary<string, string?>
        {
            ["Frontend:BaseUrl"] = "https://test.themiragehub.com/"
        });

        await service.SendWelcomeEmailAsync("person@example.com", "Person");

        Assert.NotNull(transport.LastMessage);
        Assert.Contains("href=\"https://test.themiragehub.com", transport.LastMessage.Html);
        Assert.DoesNotContain("{{APP_URL}}", transport.LastMessage.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("vercel.app", transport.LastMessage.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(CelebrationType.Birthday, "Happy birthday")]
    [InlineData(CelebrationType.Anniversary, "Happy anniversary")]
    public async Task Send_CelebrationEmail_UsesDedicatedContent(CelebrationType type, string expectedSubject)
    {
        var transport = new StubTransport("ZeptoMail", configured: true, succeeds: true);
        var service = CreateService([
            transport,
            new StubTransport("AmazonSes", configured: false, succeeds: false),
            new StubTransport("Mailjet", configured: false, succeeds: false)
        ]);

        await service.SendCelebrationEmailAsync("person@example.com", "Person", type,
            "https://www.themiragehub.com/testimonials/123");

        Assert.NotNull(transport.LastMessage);
        Assert.Contains(expectedSubject, transport.LastMessage.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("View your celebration", transport.LastMessage.Html);
    }

    private static ResilientEmailService CreateService(IEnumerable<IEmailTransport> transports,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Email:ProviderOrder:0"] = "ZeptoMail",
            ["Email:ProviderOrder:1"] = "AmazonSes",
            ["Email:ProviderOrder:2"] = "Mailjet",
            ["SocialMedia:Instagram"] = "https://www.instagram.com/themiragehub"
        };
        if (overrides is not null)
            foreach (var (key, value) in overrides) values[key] = value;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
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
        public EmailTransportMessage? LastMessage { get; private set; }

        public Task<bool> SendAsync(EmailTransportMessage message, CancellationToken cancellationToken)
        {
            SendCount++;
            LastMessage = message;
            return Task.FromResult(succeeds);
        }
    }
}
