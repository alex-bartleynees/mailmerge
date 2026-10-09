using System.Text.Json;
using MailMerge.Api.Features.Accounts;
using MailMerge.Api.Features.Accounts.Microsoft365;
using MailMerge.Api.Features.Accounts.Sending;
using Microsoft.Extensions.Logging.Abstractions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace MailMerge.IntegrationTests.Senders;

/// <summary>GraphMailSender against a fake Graph (WireMock). Ports test_graph_* and covers the send-retry rules.</summary>
public sealed class GraphMailSenderTests : IDisposable
{
    private readonly WireMockServer _graph = WireMockServer.Start();
    private readonly FakeTokens _tokens = new();

    private sealed class FakeTokens : IMicrosoftTokenProvider
    {
        public Exception? Failure { get; set; }

        public Task<string> GetAccessTokenAsync(SendingAccount account, CancellationToken ct) =>
            Failure is null ? Task.FromResult("test-token") : Task.FromException<string>(Failure);
    }

    public void Dispose() => _graph.Stop();

    private GraphMailSender Sender() => new(
        new SendingAccount { Id = 1, Name = "Shop", Kind = AccountKind.Microsoft },
        new HttpClient(),
        _tokens,
        new MicrosoftSettings { GraphBaseUrl = _graph.Url! + "/v1.0" },
        TimeProvider.System,
        NullLogger<GraphMailSender>.Instance);

    private static OutgoingMessage Message(bool html = false, params OutgoingAttachment[] attachments) =>
        new("ann@example.com", "Hello Ann", html ? "<p>Hi</p>" : "Hi", html, attachments);

    private void DraftCreated(string id = "draft-1") =>
        _graph.Given(Request.Create().WithPath("/v1.0/me/messages").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBodyAsJson(new { id }));

    private int Calls(string path, string method) =>
        _graph.LogEntries.Count(e => e.RequestMessage.Path == path && e.RequestMessage.Method == method);

    [Fact]
    public async Task Creates_a_draft_with_inline_and_chunked_attachments_then_sends_it()
    {
        DraftCreated();
        _graph.Given(Request.Create().WithPath("/v1.0/me/messages/draft-1/attachments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBodyAsJson(new { id = "att" }));
        _graph.Given(Request.Create().WithPath("/v1.0/me/messages/draft-1/attachments/createUploadSession").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBodyAsJson(new { uploadUrl = _graph.Url + "/upload/abc" }));
        _graph.Given(Request.Create().WithPath("/upload/abc").UsingPut()).RespondWith(Response.Create().WithStatusCode(200));
        _graph.Given(Request.Create().WithPath("/v1.0/me/messages/draft-1/send").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(202));

        var big = new byte[GraphMailSender.InlineAttachmentMax + 10];
        var sender = Sender();
        var id = await sender.CreateDraftAsync(Message(html: true,
            new OutgoingAttachment("price list.txt", "text/plain", "hello"u8.ToArray()),
            new OutgoingAttachment("catalogue.pdf", "application/pdf", big)), CancellationToken.None);
        await sender.SendDraftAsync(id, CancellationToken.None);

        id.Should().Be("draft-1");
        var create = _graph.LogEntries.First(e => e.RequestMessage.Path == "/v1.0/me/messages");
        create.RequestMessage.Headers!["Authorization"].Should().Equal("Bearer test-token");
        var payload = JsonDocument.Parse(create.RequestMessage.Body!).RootElement;
        payload.GetProperty("subject").GetString().Should().Be("Hello Ann");
        payload.GetProperty("body").GetProperty("contentType").GetString().Should().Be("HTML");
        payload.GetProperty("toRecipients")[0].GetProperty("emailAddress").GetProperty("address").GetString()
            .Should().Be("ann@example.com");

        var inline = JsonDocument.Parse(_graph.LogEntries
            .First(e => e.RequestMessage.Path == "/v1.0/me/messages/draft-1/attachments").RequestMessage.Body!).RootElement;
        inline.GetProperty("name").GetString().Should().Be("price list.txt");
        Convert.FromBase64String(inline.GetProperty("contentBytes").GetString()!).Should().Equal("hello"u8.ToArray());

        var chunks = _graph.LogEntries.Where(e => e.RequestMessage.Path == "/upload/abc").ToList();
        chunks.Should().HaveCount((int)Math.Ceiling(big.Length / (double)GraphMailSender.ChunkSize));
        chunks[0].RequestMessage.Headers!["Content-Range"].Single()
            .Should().Be($"bytes 0-{GraphMailSender.ChunkSize - 1}/{big.Length}");
        chunks.Should().OnlyContain(c => !c.RequestMessage.Headers!.ContainsKey("Authorization"),
            "upload URLs are pre-authorised and reject bearer tokens");
        Calls("/v1.0/me/messages/draft-1/send", "POST").Should().Be(1);
    }

    [Fact]
    public async Task Plain_text_body_uses_the_text_content_type()
    {
        DraftCreated();

        await Sender().CreateDraftAsync(Message(), CancellationToken.None);

        var payload = JsonDocument.Parse(_graph.LogEntries.Single().RequestMessage.Body!).RootElement;
        payload.GetProperty("body").GetProperty("contentType").GetString().Should().Be("Text");
    }

    [Fact]
    public async Task Throttling_is_retried_after_the_retry_after_delay()
    {
        _graph.Given(Request.Create().WithPath("/v1.0/me/messages").UsingPost()).InScenario("throttle")
            .WillSetStateTo("ok")
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "0"));
        _graph.Given(Request.Create().WithPath("/v1.0/me/messages").UsingPost()).InScenario("throttle")
            .WhenStateIs("ok")
            .RespondWith(Response.Create().WithStatusCode(201).WithBodyAsJson(new { id = "draft-2" }));

        var id = await Sender().CreateDraftAsync(Message(), CancellationToken.None);

        id.Should().Be("draft-2");
        Calls("/v1.0/me/messages", "POST").Should().Be(2);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Denied_is_an_account_problem(int status)
    {
        _graph.Given(Request.Create().WithPath("/v1.0/me/messages").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(status)
                .WithBodyAsJson(new { error = new { message = "Access is denied." } }));

        var act = () => Sender().CreateDraftAsync(Message(), CancellationToken.None);

        (await act.Should().ThrowAsync<AccountProblemException>()).WithMessage("*Access is denied.*");
    }

    [Fact]
    public async Task Not_signed_in_surfaces_as_an_account_problem()
    {
        _tokens.Failure = new AccountProblemException("Not signed in to Microsoft.");

        var act = () => Sender().CreateDraftAsync(Message(), CancellationToken.None);

        await act.Should().ThrowAsync<AccountProblemException>().WithMessage("Not signed in*");
    }

    [Theory]
    [InlineData(504)]
    [InlineData(503)]
    [InlineData(500)]
    [InlineData(404)]
    public async Task Send_errors_that_may_hide_a_success_are_uncertain_and_not_retried(int status)
    {
        _graph.Given(Request.Create().WithPath("/v1.0/me/messages/draft-1/send").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(status));

        var act = () => Sender().SendDraftAsync("draft-1", CancellationToken.None);

        await act.Should().ThrowAsync<DeliveryUncertainException>().WithMessage("*Check Sent Items*");
        Calls("/v1.0/me/messages/draft-1/send", "POST").Should().Be(1, "retrying could send the email twice");
    }

    [Fact]
    public async Task Send_rejected_with_400_is_a_definite_failure()
    {
        _graph.Given(Request.Create().WithPath("/v1.0/me/messages/draft-1/send").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(400).WithBodyAsJson(new { error = new { message = "Bad recipient" } }));

        var act = () => Sender().SendDraftAsync("draft-1", CancellationToken.None);

        await act.Should().ThrowAsync<MessageRejectedException>().WithMessage("*Bad recipient*");
    }

    [Fact]
    public async Task Draft_creation_retries_gateway_errors()
    {
        _graph.Given(Request.Create().WithPath("/v1.0/me/messages").UsingPost()).InScenario("busy")
            .WillSetStateTo("ok")
            .RespondWith(Response.Create().WithStatusCode(503).WithHeader("Retry-After", "0"));
        _graph.Given(Request.Create().WithPath("/v1.0/me/messages").UsingPost()).InScenario("busy").WhenStateIs("ok")
            .RespondWith(Response.Create().WithStatusCode(201).WithBodyAsJson(new { id = "draft-3" }));

        (await Sender().CreateDraftAsync(Message(), CancellationToken.None)).Should().Be("draft-3");
    }

    [Fact]
    public async Task A_failed_attachment_removes_the_half_built_draft()
    {
        DraftCreated();
        _graph.Given(Request.Create().WithPath("/v1.0/me/messages/draft-1/attachments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(400).WithBodyAsJson(new { error = new { message = "too big" } }));
        _graph.Given(Request.Create().WithPath("/v1.0/me/messages/draft-1").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(204));

        var act = () => Sender().CreateDraftAsync(
            Message(false, new OutgoingAttachment("a.txt", "text/plain", [1])), CancellationToken.None);

        await act.Should().ThrowAsync<MessageRejectedException>();
        Calls("/v1.0/me/messages/draft-1", "DELETE").Should().Be(1);
    }

    [Fact]
    public async Task Check_reports_the_mailbox()
    {
        _graph.Given(Request.Create().WithPath("/v1.0/me").UsingGet())
            .RespondWith(Response.Create().WithBodyAsJson(new { mail = "sales@shop.test", userPrincipalName = "x" }));

        (await Sender().CheckAsync(CancellationToken.None)).Should().Be("Connected as sales@shop.test");
    }
}
