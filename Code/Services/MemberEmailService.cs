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

    public async Task SendSignupConfirmationEmailAsync(string email, MemberEmailData memberData, IEnumerable<string> selectedCrewNames, string subjectTemplate, string bodyTemplate)
    {
        memberData.SelectedCrews = string.Join(", ", selectedCrewNames);

        var subject = ProcessSignupTemplate(subjectTemplate, memberData);
        var body = ProcessSignupTemplate(bodyTemplate, memberData);
        body = WrapInHtml(body);

        await SendEmailAsync(email, subject, body);
    }

    public async Task SendSupervisorNotificationEmailAsync(string supervisorEmail, string supervisorName, MemberEmailData memberData, string crewName, string subjectTemplate, string bodyTemplate)
    {
        // Set supervisor-specific fields
        memberData.SupervisorName = supervisorName;
        memberData.CrewName = crewName;

        var subject = ProcessSupervisorTemplate(subjectTemplate, memberData);
        var body = ProcessSupervisorTemplate(bodyTemplate, memberData);
        body = WrapInHtml(body);

        await SendEmailAsync(supervisorEmail, subject, body);
    }

    public async Task SendInvitationEmailAsync(string email, MemberEmailData memberData, string invitationUrl, string subjectTemplate, string bodyTemplate)
    {
        var subject = ProcessTemplate(subjectTemplate, memberData, invitationUrl);
        var body = ProcessTemplate(bodyTemplate, memberData, invitationUrl);
        body = WrapInHtml(body);

        // Use broadcast stream for invitation emails (bulk sends)
        await SendEmailAsync(email, subject, body, useBroadcast: true);
    }

    public async Task SendAcceptanceConfirmationEmailAsync(string email, MemberEmailData memberData, IEnumerable<string> selectedCrewNames, string subjectTemplate, string bodyTemplate)
    {
        memberData.SelectedCrews = string.Join(", ", selectedCrewNames);

        var subject = ProcessSignupTemplate(subjectTemplate, memberData);
        var body = ProcessSignupTemplate(bodyTemplate, memberData);
        body = WrapInHtml(body);

        await SendEmailAsync(email, subject, body);
    }

    public async Task SendCrewMessageNotificationAsync(string toEmail, string recipientName, string authorName, string crewName, string messageHtml, string crewUrl)
    {
        var subject = $"Ny besked i {crewName}-crew — Blue Bridge Frivillig";
        var body = $@"
<html>
<body style=""margin: 0; padding: 0; background-color: #f1f5f9; font-family: Arial, sans-serif;"">
  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background-color: #f1f5f9; padding: 32px 0;"">
    <tr>
      <td align=""center"">
        <table width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width: 600px; width: 100%;"">
          <!-- Header -->
          <tr>
            <td style=""background-color: #23297A; padding: 24px 32px; text-align: center;"">
              <span style=""color: #ffffff; font-size: 22px; font-weight: bold; letter-spacing: 1px;"">BLUE BRIDGE</span>
              <span style=""color: #EE746D; font-size: 12px; font-weight: 300; letter-spacing: 3px; text-transform: uppercase; margin-left: 8px;"">Frivillig</span>
            </td>
          </tr>
          <!-- Yellow accent bar -->
          <tr>
            <td style=""background-color: #EE746D; height: 4px; font-size: 0; line-height: 0;"">&nbsp;</td>
          </tr>
          <!-- Body -->
          <tr>
            <td style=""background-color: #ffffff; padding: 32px;"">
              <p style=""margin: 0 0 16px; font-size: 16px; color: #1e293b;"">Hej {recipientName},</p>
              <p style=""margin: 0 0 20px; font-size: 15px; color: #334155;""><strong>{authorName}</strong> har skrevet en ny besked i <strong>{crewName}</strong>:</p>
              <!-- Message quote -->
              <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin: 0 0 24px;"">
                <tr>
                  <td style=""border-left: 4px solid #23297A; background-color: #f8fafc; padding: 16px 20px; font-size: 14px; color: #334155; line-height: 1.6;"">
                    {messageHtml}
                  </td>
                </tr>
              </table>
              <!-- CTA Button -->
              <table cellpadding=""0"" cellspacing=""0"" style=""margin: 0 auto 8px;"">
                <tr>
                  <td align=""center"" style=""background-color: #23297A; border-radius: 6px;"">
                    <a href=""{crewUrl}"" style=""display: inline-block; padding: 12px 28px; color: #ffffff; font-size: 15px; font-weight: bold; text-decoration: none; letter-spacing: 0.5px;"" target=""_blank"">
                      Gå til crew-siden
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>
          <!-- Footer -->
          <tr>
            <td style=""background-color: #23297A; padding: 20px 32px; text-align: center;"">
              <p style=""margin: 0; font-size: 12px; color: #94a3b8;"">Du modtager denne email, fordi du er tilknyttet {crewName}-crew.</p>
              <p style=""margin: 8px 0 0; font-size: 11px; color: #64748b;"">Blue Bridge Festival</p>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>";

        await SendEmailAsync(toEmail, subject, body, useBroadcast: true);
    }

    private async Task SendEmailAsync(string toEmail, string subject, string htmlBody, bool useBroadcast = false)
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

            // Add Postmark broadcast stream header for bulk emails
            if (useBroadcast && !string.IsNullOrEmpty(_emailSettings.BroadcastStreamId))
            {
                message.Headers.Add("X-PM-Message-Stream", _emailSettings.BroadcastStreamId);
                _logger.LogDebug("Using Postmark broadcast stream: {StreamId}", _emailSettings.BroadcastStreamId);
            }

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

    private string ProcessSupervisorTemplate(string template, MemberEmailData memberData)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        var result = template;

        // Replace all member and supervisor field placeholders
        result = ReplaceMemberPlaceholders(result, memberData);
        result = ReplacePlaceholder(result, "supervisorName", memberData.SupervisorName);
        result = ReplacePlaceholder(result, "crewName", memberData.CrewName);

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
        result = ReplacePlaceholder(result, "memberWish", memberData.MemberWish);
        result = ReplacePlaceholder(result, "timeslotWishes", memberData.TimeslotWishes);

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

    private static string WrapInHtml(string body)
    {
        if (body.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return body;
        }

        return $@"
            <html>
            <body>
                {body}
            </body>
            </html>";
    }
}
