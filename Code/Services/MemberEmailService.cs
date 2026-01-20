using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Code.Services;

public class MemberEmailService : IMemberEmailService
{
    private readonly ILogger<MemberEmailService> _logger;
    private readonly EmailSettings _emailSettings;

    public MemberEmailService(ILogger<MemberEmailService> logger, IOptions<EmailSettings> emailSettings)
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

        // Wrap body in basic HTML structure if not already wrapped
        if (!body.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            body = $@"
                <html>
                <body>
                    {body}
                </body>
                </html>";
        }

        await SendEmailAsync(email, subject, body);
    }

    public async Task SendAcceptanceConfirmationEmailAsync(string email, MemberEmailData memberData, IEnumerable<string> selectedCrewNames, string subjectTemplate, string bodyTemplate)
    {
        // Set the selected crews on memberData for template processing
        memberData.SelectedCrews = string.Join(", ", selectedCrewNames);

        var subject = ProcessSignupTemplate(subjectTemplate, memberData);
        var body = ProcessSignupTemplate(bodyTemplate, memberData);

        // Wrap body in basic HTML structure if not already wrapped
        if (!body.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            body = $@"
                <html>
                <body>
                    {body}
                </body>
                </html>";
        }

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

            _logger.LogInformation("Email sent successfully to {Email} with subject: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email} with subject: {Subject}", toEmail, subject);
            throw;
        }
    }

    private string ProcessTemplate(string template, MemberEmailData memberData, string invitationUrl)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        var result = template;

        // Replace member field placeholders {{ fieldName }}
        result = ReplaceMemberPlaceholders(result, memberData);

        // Replace {{ invitationUrl }} with a styled button
        var buttonHtml = CreateStyledButton("Tilmeld mig som frivillig", invitationUrl);
        result = ReplacePlaceholder(result, "invitationUrl", buttonHtml);

        return result;
    }

    private string ProcessSignupTemplate(string template, MemberEmailData memberData)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        var result = template;

        // Replace member field placeholders {{ fieldName }}
        result = ReplaceMemberPlaceholders(result, memberData);

        return result;
    }

    private string ReplaceMemberPlaceholders(string template, MemberEmailData memberData)
    {
        var result = template;

        result = ReplacePlaceholder(result, "email", memberData.Email);
        result = ReplacePlaceholder(result, "username", memberData.Username);
        result = ReplacePlaceholder(result, "firstName", memberData.FirstName);
        result = ReplacePlaceholder(result, "lastName", memberData.LastName);
        result = ReplacePlaceholder(result, "phone", memberData.Phone);
        result = ReplacePlaceholder(result, "zipcode", memberData.Zipcode);
        result = ReplacePlaceholder(result, "tidligereArbejdssteder", memberData.TidligereArbejdssteder);
        result = ReplacePlaceholder(result, "selectedCrews", memberData.SelectedCrews);

        // Replace {{ portalUrl }} with a styled button linking to the login page
        if (!string.IsNullOrEmpty(memberData.PortalUrl))
        {
            var loginUrl = $"{memberData.PortalUrl.TrimEnd('/')}/login";
            var portalButtonHtml = CreateStyledButton("Gå til portalen", loginUrl);
            result = ReplacePlaceholder(result, "portalUrl", portalButtonHtml);
        }

        return result;
    }

    private static string ReplacePlaceholder(string template, string fieldName, string value)
    {
        // Match {{ fieldName }} with optional whitespace
        var pattern = @"\{\{\s*" + Regex.Escape(fieldName) + @"\s*\}\}";
        return Regex.Replace(template, pattern, value ?? string.Empty, RegexOptions.IgnoreCase);
    }

    private static string CreateStyledButton(string label, string url)
    {
        return $@"<table border=""0"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""margin: 20px 0;"">
  <tr>
    <td align=""center"" bgcolor=""#007bff"" role=""presentation"" style=""border: none; border-radius: 6px; cursor: pointer; mso-padding-alt: 12px 24px;"">
      <a href=""{url}"" style=""background: #007bff; border-radius: 6px; color: #ffffff; display: inline-block; font-family: Arial, sans-serif; font-size: 16px; font-weight: bold; line-height: 1.5; padding: 12px 24px; text-decoration: none; text-align: center;"" target=""_blank"">
        {label}
      </a>
    </td>
  </tr>
</table>";
    }
}
