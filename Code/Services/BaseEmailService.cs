using System.Text.RegularExpressions;

namespace Code.Services;

public abstract class BaseEmailService
{
    protected string ProcessTemplate(string template, MemberEmailData memberData, string invitationUrl)
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

    protected string ProcessSignupTemplate(string template, MemberEmailData memberData)
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

    protected string ReplaceMemberPlaceholders(string template, MemberEmailData memberData)
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

    protected static string ReplacePlaceholder(string template, string fieldName, string value)
    {
        // Match {{ fieldName }} with optional whitespace
        var pattern = @"\{\{\s*" + Regex.Escape(fieldName) + @"\s*\}\}";
        return Regex.Replace(template, pattern, value ?? string.Empty, RegexOptions.IgnoreCase);
    }

    protected static string CreateStyledButton(string label, string url)
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

    protected static string WrapInHtml(string body)
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
