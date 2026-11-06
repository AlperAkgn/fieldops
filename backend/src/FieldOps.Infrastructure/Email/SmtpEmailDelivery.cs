using FieldOps.Application.Identity;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FieldOps.Infrastructure.Email;

public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string FromAddress { get; set; } = "noreply@fieldops.local";
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public bool UseStartTls { get; set; } = true;
}

internal sealed class SmtpEmailDelivery(IOptions<SmtpOptions> options) : IEmailDelivery
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var settings = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("FieldOps", settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.ToEmail));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };
        using var smtp = new SmtpClient { Timeout = 5000 };
        await smtp.ConnectAsync(settings.Host, settings.Port,
            settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, timeout.Token);
        if (!string.IsNullOrWhiteSpace(settings.UserName))
            await smtp.AuthenticateAsync(settings.UserName,
                settings.Password ?? throw new InvalidOperationException("SMTP credentials are incomplete."), timeout.Token);
        await smtp.SendAsync(mime, timeout.Token);
        await smtp.DisconnectAsync(true, timeout.Token);
    }
}
