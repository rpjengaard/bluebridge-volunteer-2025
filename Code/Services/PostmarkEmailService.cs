using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostmarkDotNet;

namespace Code.Services;

public class PostmarkEmailService : BaseEmailService, IMemberEmailService
{
    private readonly ILogger<PostmarkEmailService> _logger;
    private readonly EmailSettings _emailSettings;
    private readonly PostmarkClient _client;

    public PostmarkEmailService(ILogger<PostmarkEmailService> logger, IOptions<EmailSettings> emailSettings)
    {
        _logger = logger;
        _emailSettings = emailSettings.Value;

        if (string.IsNullOrEmpty(_emailSettings.PostmarkServerToken))
        {
            throw new InvalidOperationException("PostmarkServerToken is required when using Postmark email provider");
        }

        _client = new PostmarkClient(_emailSettings.PostmarkServerToken);
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
            var message = new PostmarkMessage
            {
                From = $"{_emailSettings.FromName} <{_emailSettings.FromEmail}>",
                To = toEmail,
                Subject = subject,
                HtmlBody = htmlBody,
                TrackOpens = true
            };

            var result = await _client.SendMessageAsync(message);

            if (result.Status == PostmarkStatus.Success)
            {
                _logger.LogInformation("Email sent successfully via Postmark to {Email} with subject: {Subject}, MessageId: {MessageId}",
                    toEmail, subject, result.MessageID);
            }
            else
            {
                _logger.LogError("Failed to send email via Postmark to {Email}. Status: {Status}, Message: {Message}",
                    toEmail, result.Status, result.Message);
                throw new InvalidOperationException($"Postmark email failed: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email via Postmark to {Email} with subject: {Subject}", toEmail, subject);
            throw;
        }
    }
}
