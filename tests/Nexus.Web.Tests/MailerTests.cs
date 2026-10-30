using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Pins what Mailer puts on the wire, against a minimal SMTP server on loopback.
/// </summary>
/// <remarks>
/// E-mail failures are designed to be silent (callers carry on when a send returns false), and
/// that already let production run for weeks without delivering anything. A real SMTP exchange
/// here is the cheapest way to notice when the message stops going out.
/// </remarks>
public class MailerTests
{
    /// <summary>
    /// Accepts one connection and speaks just enough SMTP for MailKit: no STARTTLS, no AUTH.
    /// Answers RCPT TO with <paramref name="rcptReply"/> so a rejection can be simulated.
    /// </summary>
    private sealed class FakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly Task serving;
        private readonly string rcptReply;

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public string? MailFrom { get; private set; }
        public List<string> RcptTo { get; } = [];
        public MimeMessage? Message { get; private set; }

        public FakeSmtpServer(string rcptReply = "250 2.1.5 Ok")
        {
            this.rcptReply = rcptReply;
            listener.Start();
            serving = ServeOneAsync();
        }

        private async Task ServeOneAsync()
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

            await writer.WriteLineAsync("220 fake ESMTP");
            while (await reader.ReadLineAsync() is { } line)
            {
                var verb = line.Split(' ', 2)[0].ToUpperInvariant();
                switch (verb)
                {
                    case "EHLO":
                        await writer.WriteLineAsync("250-fake");
                        await writer.WriteLineAsync("250 8BITMIME");
                        break;
                    case "MAIL":
                        MailFrom = line;
                        await writer.WriteLineAsync("250 2.1.0 Ok");
                        break;
                    case "RCPT":
                        RcptTo.Add(line);
                        await writer.WriteLineAsync(rcptReply);
                        break;
                    case "DATA":
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                        var data = new StringBuilder();
                        while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                        {
                            data.Append(dataLine.StartsWith("..") ? dataLine[1..] : dataLine).Append("\r\n");
                        }
                        Message = MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(data.ToString())));
                        await writer.WriteLineAsync("250 2.0.0 Ok: queued");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 2.0.0 Bye");
                        return;
                    default:
                        // RSET, NOOP and anything else MailKit may send between steps.
                        await writer.WriteLineAsync("250 Ok");
                        break;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            listener.Stop();
            try { await serving; } catch { /* the client may already be gone */ }
        }
    }

    private static Mailer CreateMailer(int port, string host = "127.0.0.1", string sender = "nao-responda@aamora.com.br")
        => new(
            Options.Create(new SmtpOptions { Host = host, Port = port, SenderEmail = sender, SenderName = "Nexus" }),
            NullLogger<Mailer>.Instance);

    [Fact]
    public async Task Sends_a_multipart_message_from_the_configured_sender()
    {
        await using var server = new FakeSmtpServer();

        var sent = await CreateMailer(server.Port).SendAsync("alguem@example.com", "Assunto", "<p>Corpo</p>", "Corpo");

        Assert.True(sent);
        Assert.Contains("<nao-responda@aamora.com.br>", server.MailFrom);
        Assert.Contains("<alguem@example.com>", Assert.Single(server.RcptTo));

        var message = server.Message!;
        var from = Assert.Single(message.From.Mailboxes);
        Assert.Equal("Nexus", from.Name);
        Assert.Equal("nao-responda@aamora.com.br", from.Address);
        Assert.Equal("alguem@example.com", Assert.Single(message.To.Mailboxes).Address);
        Assert.Equal("Assunto", message.Subject);
        Assert.Equal("<p>Corpo</p>", message.HtmlBody?.Trim());
        Assert.Equal("Corpo", message.TextBody?.Trim());
    }

    [Fact]
    public async Task Returns_false_when_the_server_rejects_the_recipient()
    {
        await using var server = new FakeSmtpServer("554 5.7.1 Relay access denied");

        var sent = await CreateMailer(server.Port).SendAsync("alguem@example.com", "Assunto", "<p>Corpo</p>");

        Assert.False(sent);
    }

    [Fact]
    public async Task Returns_false_when_the_server_is_unreachable()
    {
        // Grab a free port and release it, so nothing is listening there.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var sent = await CreateMailer(port).SendAsync("alguem@example.com", "Assunto", "<p>Corpo</p>");

        Assert.False(sent);
    }

    [Theory]
    [InlineData("", "nao-responda@aamora.com.br")]
    [InlineData("127.0.0.1", "")]
    public async Task Does_not_connect_when_it_is_not_configured(string host, string sender)
    {
        await using var server = new FakeSmtpServer();

        var sent = await CreateMailer(server.Port, host, sender).SendAsync("alguem@example.com", "Assunto", "<p>Corpo</p>");

        Assert.False(sent);
        Assert.Null(server.MailFrom);
    }
}
