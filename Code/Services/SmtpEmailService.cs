using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Code.Services;

public class SmtpEmailService : BaseEmailService, IMemberEmailService
{
    private readonly ILogger<SmtpEmailService> _logger;
    private readonly EmailSettings _emailSettings;

    public SmtpEmailService(ILogger<SmtpEmailService> logger, IOptions<EmailSettings> emailSettings)
    {
        _logger = logger;
        _emailSettings = emailSettings.Value;
    }

    public async Task SendPasswordResetEmailAsync(string email, string resetUrl)
    {
        var subject = "Blue Bridge - Nulstil din adgangskode";
        var body = $@"
            <html>
            <body>
                <p>Du har anmodet om at nulstille din adgangskode.</p>
                <p>Klik på linket herunder for at nulstille din adgangskode:</p>
                <p><a href=""{resetUrl}"">{resetUrl}</a></p>
                <p>Hvis du ikke har anmodet om dette, kan du ignorere denne email.</p>
            </body>
            </html>";

        await SendEmailAsync(email, subject, body);
    }

    public async Task SendWelcomeEmailAsync(string email, string firstName)
    {
        var subject = "Velkommen til Blue Bridge Portal";
        var body = $@"
            <html>
            <body>
                <p>Kære {firstName},</p>
                <p>Velkommen til Blue Bridge Frivillig Portal!</p>
                <p>Du kan nu logge ind og se dine oplysninger.</p>
            </body>
            </html>";

        await SendEmailAsync(email, subject, body);
    }

    public async Task SendInvitationEmailAsync(string email, MemberEmailData memberData, string invitationUrl, string subjectTemplate, string bodyTemplate)
    {
        var subject = ProcessTemplate(subjectTemplate, memberData, invitationUrl);
        var body = ProcessTemplate(bodyTemplate, memberData, invitationUrl);
        body = WrapInHtml(body);

        await SendEmailAsync(email, subject, body);
    }

    public async Task SendAcceptanceConfirmationEmailAsync(string email, MemberEmailData memberData, IEnumerable<string> selectedCrewNames, string subjectTemplate, string bodyTemplate)
    {
        memberData.SelectedCrews = string.Join(", ", selectedCrewNames);

        var subject = ProcessSignupTemplate(subjectTemplate, memberData);
        var body = ProcessSignupTemplate(bodyTemplate, memberData);
        body = WrapInHtml(body);

        await SendEmailAsync(email, subject, body);
    }

    private async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        try
        {
            using var client = new SmtpClient(_emailSettings.SmtpHost, _emailSettings.SmtpPort);
            client.EnableSsl = _emailSettings.EnableSsl;

            if (!string.IsNullOrEmpty(_emailSettings.SmtpUsername))
            {
                client.Credentials = new NetworkCredential(_emailSettings.SmtpUsername, _emailSettings.SmtpPassword);
            }

            var from = new MailAddress(_emailSettings.FromEmail, _emailSettings.FromName);
            var to = new MailAddress(toEmail);

            using var message = new MailMessage(from, to)
            {
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            await client.SendMailAsync(message);

            _logger.LogInformation("Email sent successfully via SMTP to {Email} with subject: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email via SMTP to {Email} with subject: {Subject}", toEmail, subject);
            throw;
        }
    }
}
