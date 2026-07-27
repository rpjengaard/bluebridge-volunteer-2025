using Code.Migrations;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Cms.Web.Common.Security;
using Umbraco.Extensions;

namespace Code.Services;

// [CHANGE: crew invitation feature - shiftadmins invite new volunteers while signup is closed]
// Related: Code/Migrations/AddCrewInvitationTableMigration.cs, Web/Controllers/CrewInvitationSurfaceController.cs, Code/Services/MemberEmailService.cs

public class CrewInvitationService : ICrewInvitationService
{
    private readonly IScopeProvider _scopeProvider;
    private readonly IMemberService _memberService;
    private readonly IMemberGroupService _memberGroupService;
    private readonly IMemberManager _memberManager;
    private readonly IMemberSignInManager _memberSignInManager;
    private readonly IContentService _contentService;
    private readonly IPublishedContentQuery _publishedContentQuery;
    private readonly IMemberEmailService _emailService;
    private readonly IInvitationService _invitationService;
    private readonly ILogger<CrewInvitationService> _logger;

    private const string MemberGroupName = "Frivillige";
    private const int InvitationValidDays = 14;

    public CrewInvitationService(
        IScopeProvider scopeProvider,
        IMemberService memberService,
        IMemberGroupService memberGroupService,
        IMemberManager memberManager,
        IMemberSignInManager memberSignInManager,
        IContentService contentService,
        IPublishedContentQuery publishedContentQuery,
        IMemberEmailService emailService,
        IInvitationService invitationService,
        ILogger<CrewInvitationService> logger)
    {
        _scopeProvider = scopeProvider;
        _memberService = memberService;
        _memberGroupService = memberGroupService;
        _memberManager = memberManager;
        _memberSignInManager = memberSignInManager;
        _contentService = contentService;
        _publishedContentQuery = publishedContentQuery;
        _emailService = emailService;
        _invitationService = invitationService;
        _logger = logger;
    }

    public async Task<CrewInvitationSendResult> SendInvitationAsync(int crewId, string email, string firstName, string lastName, string inviterEmail, string baseUrl)
    {
        email = email.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return new CrewInvitationSendResult { Success = false, Message = "Ugyldig email-adresse." };
        }

        var crewContent = _contentService.GetById(crewId);
        if (crewContent == null)
        {
            return new CrewInvitationSendResult { Success = false, Message = "Crew blev ikke fundet." };
        }

        // Existing member? Active members are rejected; inactive members get the existing member-invitation flow.
        var existingMember = _memberService.GetByEmail(email);
        if (existingMember != null)
        {
            if (existingMember.GetValue<bool>("accept2026"))
            {
                return new CrewInvitationSendResult
                {
                    Success = false,
                    Message = $"{email} er allerede tilmeldt som frivillig."
                };
            }

            var reinviteResult = await _invitationService.SendInvitationAsync(existingMember.Id, baseUrl);
            return new CrewInvitationSendResult
            {
                Success = reinviteResult.Success,
                Message = reinviteResult.Success
                    ? $"{email} findes allerede som tidligere frivillig – en gen-invitation er sendt i stedet."
                    : $"Kunne ikke sende gen-invitation til {email}: {reinviteResult.Message}"
            };
        }

        var inviter = _memberService.GetByEmail(inviterEmail);
        var inviterName = GetMemberFullName(inviter) ?? inviterEmail;

        var token = Guid.NewGuid().ToString("N");

        using (var scope = _scopeProvider.CreateScope(autoComplete: true))
        {
            var db = scope.Database;

            // Replace any previous non-accepted invitation for the same email + crew
            var existing = db.SingleOrDefault<CrewInvitationSchema>(
                "SELECT * FROM BbvCrewInvitation WHERE Email = @0 AND CrewId = @1 AND AcceptedDate IS NULL", email, crewId);

            if (existing != null)
            {
                existing.FirstName = firstName;
                existing.LastName = lastName;
                existing.Token = token;
                existing.InvitedByEmail = inviterEmail;
                existing.InvitedByName = inviterName;
                existing.SentDate = DateTime.Now;
                existing.CanceledDate = null;
                db.Update(existing);
            }
            else
            {
                db.Insert(new CrewInvitationSchema
                {
                    Email = email,
                    FirstName = firstName,
                    LastName = lastName,
                    CrewId = crewId,
                    CrewKey = crewContent.Key,
                    Token = token,
                    InvitedByEmail = inviterEmail,
                    InvitedByName = inviterName,
                    SentDate = DateTime.Now,
                    CreatedUtc = DateTime.UtcNow
                });
            }
        }

        try
        {
            await SendInvitationEmailAsync(email, firstName, crewContent.Name ?? "Blue Bridge", inviterName, token, baseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send crew invitation email to {Email}", email);
            return new CrewInvitationSendResult { Success = false, Message = "Invitationen kunne ikke sendes. Prøv igen senere." };
        }

        _logger.LogInformation("Crew invitation sent to {Email} for crew {CrewId} by {Inviter}", email, crewId, inviterEmail);
        return new CrewInvitationSendResult { Success = true, Message = $"Invitationen er sendt til {email}." };
    }

    public Task<List<CrewInvitationItem>> GetInvitationsForCrewAsync(int crewId)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var rows = db.Fetch<CrewInvitationSchema>(
            "SELECT * FROM BbvCrewInvitation WHERE CrewId = @0 ORDER BY SentDate DESC", crewId);

        var items = rows.Select(r => new CrewInvitationItem
        {
            Id = r.Id,
            Email = r.Email,
            FullName = $"{r.FirstName} {r.LastName}".Trim(),
            InvitedByName = r.InvitedByName,
            SentDate = r.SentDate,
            AcceptedDate = r.AcceptedDate,
            Status = GetStatus(r)
        }).ToList();

        return Task.FromResult(items);
    }

    public async Task<CrewInvitationSendResult> ResendInvitationAsync(int invitationId, string baseUrl)
    {
        CrewInvitationSchema? row;
        var token = Guid.NewGuid().ToString("N");

        using (var scope = _scopeProvider.CreateScope(autoComplete: true))
        {
            var db = scope.Database;
            row = db.SingleOrDefault<CrewInvitationSchema>(
                "SELECT * FROM BbvCrewInvitation WHERE Id = @0", invitationId);

            if (row == null)
            {
                return new CrewInvitationSendResult { Success = false, Message = "Invitationen blev ikke fundet." };
            }

            if (row.AcceptedDate != null)
            {
                return new CrewInvitationSendResult { Success = false, Message = "Invitationen er allerede accepteret." };
            }

            row.Token = token;
            row.SentDate = DateTime.Now;
            row.CanceledDate = null;
            db.Update(row);
        }

        var crewContent = _contentService.GetById(row.CrewId);

        try
        {
            await SendInvitationEmailAsync(row.Email, row.FirstName, crewContent?.Name ?? "Blue Bridge", row.InvitedByName, token, baseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resend crew invitation email to {Email}", row.Email);
            return new CrewInvitationSendResult { Success = false, Message = "Invitationen kunne ikke sendes. Prøv igen senere." };
        }

        return new CrewInvitationSendResult { Success = true, Message = $"Invitationen er gensendt til {row.Email}." };
    }

    public Task<bool> CancelInvitationAsync(int invitationId)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var affected = db.Execute(
            "UPDATE BbvCrewInvitation SET CanceledDate = @0 WHERE Id = @1 AND AcceptedDate IS NULL", DateTime.Now, invitationId);

        return Task.FromResult(affected > 0);
    }

    public Task<CrewInvitationInfo?> GetByTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Task.FromResult<CrewInvitationInfo?>(null);

        CrewInvitationSchema? row;
        using (var scope = _scopeProvider.CreateScope(autoComplete: true))
        {
            row = scope.Database.SingleOrDefault<CrewInvitationSchema>(
                "SELECT * FROM BbvCrewInvitation WHERE Token = @0", token);
        }

        if (row == null || GetStatus(row) != "Pending")
            return Task.FromResult<CrewInvitationInfo?>(null);

        var crewContent = _contentService.GetById(row.CrewId);
        if (crewContent == null)
            return Task.FromResult<CrewInvitationInfo?>(null);

        return Task.FromResult<CrewInvitationInfo?>(new CrewInvitationInfo
        {
            Id = row.Id,
            Email = row.Email,
            FirstName = row.FirstName,
            LastName = row.LastName,
            CrewId = row.CrewId,
            CrewName = crewContent.Name ?? $"Crew {row.CrewId}",
            CrewAgeLimit = crewContent.GetValue<int?>("ageLimit")
        });
    }

    public async Task<CrewInvitationAcceptResult> AcceptInvitationAsync(CrewInvitationAcceptRequest request)
    {
        var info = await GetByTokenAsync(request.Token);
        if (info == null)
        {
            return new CrewInvitationAcceptResult { Success = false, Message = "Ugyldigt eller udløbet invitationslink." };
        }

        // Someone may have created an account with this email since the invite was sent
        var existingMember = await _memberManager.FindByEmailAsync(info.Email);
        if (existingMember != null)
        {
            return new CrewInvitationAcceptResult
            {
                Success = false,
                Message = "Der findes allerede en konto med denne email. Log ind i stedet."
            };
        }

        // Enforce the crew's age limit, same rule as normal signup
        if (info.CrewAgeLimit.HasValue && info.CrewAgeLimit.Value > 0)
        {
            var age = CalculateAge(request.Birthdate);
            if (age < info.CrewAgeLimit.Value)
            {
                return new CrewInvitationAcceptResult
                {
                    Success = false,
                    Message = $"Dette crew kræver, at du er mindst {info.CrewAgeLimit.Value} år."
                };
            }
        }

        var memberName = $"{request.FirstName} {request.LastName}".Trim();
        var identityUser = MemberIdentityUser.CreateNew(
            info.Email,
            info.Email,
            "bbvMember",
            isApproved: true,
            memberName);

        var createResult = await _memberManager.CreateAsync(identityUser, request.Password);
        if (!createResult.Succeeded)
        {
            var errors = createResult.Errors.Select(e => TranslateIdentityError(e.Code));
            _logger.LogWarning("Failed to create invited member {Email}: {Errors}", info.Email, string.Join(", ", errors));
            return new CrewInvitationAcceptResult { Success = false, Message = string.Join(" ", errors) };
        }

        var crewContent = _contentService.GetById(info.CrewId);
        var member = _memberService.GetByEmail(info.Email);
        if (member != null)
        {
            member.SetValue("firstName", request.FirstName);
            member.SetValue("lastName", request.LastName);
            member.SetValue("birthdate", request.Birthdate);
            member.SetValue("accept2026", true);
            member.SetValue("acceptedDate", DateTime.UtcNow);

            if (!string.IsNullOrWhiteSpace(request.Phone))
                member.SetValue("phone", request.Phone);

            if (!string.IsNullOrWhiteSpace(request.Zipcode))
                member.SetValue("zipcode", request.Zipcode);

            // Direct crew assignment - the shiftadmin's invitation implies approval
            if (crewContent != null)
            {
                member.SetValue("crews", $"umb://document/{crewContent.Key:N}");
            }

            _memberService.Save(member);
            EnsureMemberInGroup(member);
        }

        // Mark invitation accepted (single use)
        using (var scope = _scopeProvider.CreateScope(autoComplete: true))
        {
            scope.Database.Execute(
                "UPDATE BbvCrewInvitation SET AcceptedDate = @0 WHERE Id = @1", DateTime.Now, info.Id);
        }

        await SendConfirmationEmailsAsync(info, request, memberName);

        await _memberSignInManager.SignInAsync(identityUser, isPersistent: false);

        _logger.LogInformation("Crew invitation accepted: {Email} joined crew {CrewName}", info.Email, info.CrewName);

        return new CrewInvitationAcceptResult
        {
            Success = true,
            MemberName = memberName,
            CrewName = info.CrewName
        };
    }

    private async Task SendConfirmationEmailsAsync(CrewInvitationInfo info, CrewInvitationAcceptRequest request, string memberName)
    {
        // Confirmation to the new member (reuses the signed-up templates from site settings)
        try
        {
            var siteSettings = FindSiteSettings();
            var subjectTemplate = siteSettings?.Value<string>("signedUpEmailSubject");
            var bodyTemplate = siteSettings?.Value<IHtmlEncodedString>("signedUpEmailTemplate")?.ToHtmlString();

            if (!string.IsNullOrEmpty(subjectTemplate) && !string.IsNullOrEmpty(bodyTemplate))
            {
                var memberData = new MemberEmailData
                {
                    Email = info.Email,
                    Username = info.Email,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Phone = request.Phone ?? string.Empty,
                    Zipcode = request.Zipcode ?? string.Empty,
                    PortalUrl = request.PortalUrl.TrimEnd('/')
                };

                await _emailService.SendAcceptanceConfirmationEmailAsync(
                    info.Email, memberData, new[] { info.CrewName }, subjectTemplate, bodyTemplate);
            }
            else
            {
                _logger.LogWarning("Signed-up email templates not configured, skipping confirmation email for {Email}", info.Email);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send confirmation email to invited member {Email}", info.Email);
        }

        // Notify the inviting shiftadmin
        CrewInvitationSchema? row;
        using (var scope = _scopeProvider.CreateScope(autoComplete: true))
        {
            row = scope.Database.SingleOrDefault<CrewInvitationSchema>(
                "SELECT * FROM BbvCrewInvitation WHERE Id = @0", info.Id);
        }

        if (row == null || string.IsNullOrEmpty(row.InvitedByEmail))
            return;

        try
        {
            var htmlBody = $@"<p>Hej {System.Net.WebUtility.HtmlEncode(row.InvitedByName)},</p>
<p><strong>{System.Net.WebUtility.HtmlEncode(memberName)}</strong> ({System.Net.WebUtility.HtmlEncode(info.Email)}) har accepteret din invitation og er nu tilmeldt <strong>{System.Net.WebUtility.HtmlEncode(info.CrewName)}</strong>.</p>
<p>Du kan nu tildele vagter til vedkommende på crew-siden.</p>
<p>Venlig hilsen,<br>Blue Bridge Festival</p>";

            await _emailService.SendCustomEmailAsync(
                row.InvitedByEmail,
                $"{memberName} har accepteret din invitation til {info.CrewName}",
                htmlBody,
                new MemberEmailData { Email = row.InvitedByEmail, FirstName = row.InvitedByName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify inviter {Email} about accepted crew invitation", row.InvitedByEmail);
        }
    }

    private async Task SendInvitationEmailAsync(string email, string firstName, string crewName, string inviterName, string token, string baseUrl)
    {
        var acceptUrl = $"{baseUrl.TrimEnd('/')}/umbraco/surface/CrewInvitationSurface/Accept?token={token}";

        var siteSettings = FindSiteSettings();
        var subjectTemplate = siteSettings?.Value<string>("crewInvitationEmailSubject");
        var bodyTemplate = siteSettings?.Value<IHtmlEncodedString>("crewInvitationEmailTemplate")?.ToHtmlString();

        // Fallback so the feature works before the templates are added in the backoffice
        if (string.IsNullOrEmpty(subjectTemplate))
            subjectTemplate = "Du er inviteret som frivillig til Blue Bridge – {{ crewName }}";

        if (string.IsNullOrEmpty(bodyTemplate))
            bodyTemplate = @"<p>Hej {{ firstName }},</p>
<p>{{ inviterName }} har inviteret dig til at blive frivillig i <strong>{{ crewName }}</strong> på Blue Bridge Festival.</p>
<p>Klik på knappen herunder for at oprette din profil og tilmelde dig:</p>
{{ invitationUrl }}
<p>Linket er gyldigt i " + InvitationValidDays + @" dage.</p>
<p>Venlig hilsen,<br>Blue Bridge Festival</p>";

        await _emailService.SendCrewInvitationEmailAsync(email, firstName, crewName, inviterName, acceptUrl, subjectTemplate, bodyTemplate);
    }

    private IPublishedContent? FindSiteSettings()
    {
        var root = _publishedContentQuery.ContentAtRoot().ToList();
        return root.FirstOrDefault(x => x.ContentType.Alias == "bbvSiteSettings")
            ?? root.SelectMany(x => x.Descendants()).FirstOrDefault(x => x.ContentType.Alias == "bbvSiteSettings");
    }

    private static string GetStatus(CrewInvitationSchema row)
    {
        if (row.AcceptedDate != null)
            return "Accepted";
        if (row.CanceledDate != null)
            return "Canceled";
        if (row.SentDate.AddDays(InvitationValidDays) < DateTime.Now)
            return "Expired";
        return "Pending";
    }

    private static int CalculateAge(DateTime birthdate)
    {
        var today = DateTime.Today;
        var age = today.Year - birthdate.Year;
        if (birthdate.Date > today.AddYears(-age))
            age--;
        return age;
    }

    private string? GetMemberFullName(Umbraco.Cms.Core.Models.IMember? member)
    {
        if (member == null)
            return null;

        var firstName = member.GetValue<string>("firstName") ?? string.Empty;
        var lastName = member.GetValue<string>("lastName") ?? string.Empty;
        var fullName = $"{firstName} {lastName}".Trim();
        return string.IsNullOrEmpty(fullName) ? (member.Name ?? member.Email) : fullName;
    }

    private void EnsureMemberInGroup(Umbraco.Cms.Core.Models.IMember member)
    {
        var group = _memberGroupService.GetByName(MemberGroupName);
        if (group == null)
        {
            group = new Umbraco.Cms.Core.Models.MemberGroup { Name = MemberGroupName };
#pragma warning disable CS0618
            _memberGroupService.Save(group);
#pragma warning restore CS0618
            _logger.LogInformation("Created member group '{GroupName}'", MemberGroupName);
        }

        var memberGroups = _memberService.GetAllRoles(member.Id);
        if (!memberGroups.Contains(MemberGroupName))
        {
            _memberService.AssignRole(member.Id, MemberGroupName);
            _logger.LogInformation("Added member {Email} to group '{GroupName}'", member.Email, MemberGroupName);
        }
    }

    private static string TranslateIdentityError(string code)
    {
        return code switch
        {
            "PasswordTooShort" => "Adgangskoden er for kort. Mindst 10 tegn kræves.",
            "PasswordRequiresDigit" => "Adgangskoden skal indeholde mindst ét tal.",
            "PasswordRequiresLower" => "Adgangskoden skal indeholde mindst ét lille bogstav.",
            "PasswordRequiresUpper" => "Adgangskoden skal indeholde mindst ét stort bogstav.",
            "PasswordRequiresNonAlphanumeric" => "Adgangskoden skal indeholde mindst ét specialtegn.",
            "DuplicateEmail" => "Der findes allerede en bruger med denne email.",
            _ => "Der opstod en fejl. Prøv igen."
        };
    }
}
