using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Tdla.Web;

public class MailService(IConfiguration configuration, ILogger<MailService> logger)
{
    public async Task<bool> SendAsync(string recipient, string subject, string text, string? replyTo = null)
    {
        var smtp = configuration.GetSection("Smtp");
        var host = smtp["Host"];
        var username = smtp["Username"];
        var password = smtp["Password"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(smtp["From"]) ||
            (!string.IsNullOrEmpty(username) && (string.IsNullOrEmpty(password) || password == "PASSWORD_GOES_HERE")))
        {
            logger.LogWarning("Email delivery is not configured. Supply real SMTP credentials in private configuration or php/info.php.");
            return false;
        }
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(smtp["FromName"] ?? "TDLA", smtp["From"]!));
            message.To.Add(MailboxAddress.Parse(recipient));
            if (replyTo is not null) message.ReplyTo.Add(MailboxAddress.Parse(replyTo));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = text };
            using var client = new SmtpClient { Timeout = 15000 };
            var security = (smtp["Secure"] ?? "tls").ToLowerInvariant() switch
            {
                "ssl" => SecureSocketOptions.SslOnConnect,
                "tls" => SecureSocketOptions.StartTls,
                "" => SecureSocketOptions.None,
                _ => throw new InvalidOperationException("Smtp:Secure must be ssl, tls or an empty string.")
            };
            await client.ConnectAsync(host, smtp.GetValue("Port", 25), security);
            if (!string.IsNullOrEmpty(username)) await client.AuthenticateAsync(username, password!);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            return true;
        }
        catch (Exception exception)
        {
            // Do not log message contents, reset tokens, credentials or SMTP server responses.
            logger.LogWarning("Email delivery failed ({ErrorType}). Check SMTP configuration and server availability.", exception.GetType().Name);
            return false;
        }
    }
}
