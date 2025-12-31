using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;

namespace Code.Services;

public class InvitationService : IInvitationService
{
    private readonly IMemberService _memberService;
    private readonly IMemberGroupService _memberGroupService;
    private readonly IMemberManager _memberManager;
    private readonly IContentService _contentService;
    private readonly IMemberEmailService _emailService;
    private readonly ILogger<InvitationService> _logger;

    private const string MemberGroupName = "Frivillige";
    private const string CrewContentTypeAlias = "bbvCrewPage";

    public InvitationService(
        IMemberService memberService,
        IMemberGroupService memberGroupService,
        IMemberManager memberManager,
        IContentService contentService,
        IMemberEmailService emailService,
        ILogger<InvitationService> logger)
    {
        _memberService = memberService;
        _memberGroupService = memberGroupService;
        _memberManager = memberManager;
        _contentService = contentService;
        _emailService = emailService;
        _logger = logger;
    }

    public Task<string> GenerateInvitationTokenAsync(int memberId)
    {
        var member = _memberService.GetById(memberId);
        if (member == null)
        {
            throw new InvalidOperationException($"Member with ID {memberId} not found");
        }

        var token = Guid.NewGuid().ToString("N");
        member.SetValue("invitationToken", token);
        member.SetValue("invitationSentDate", DateTime.Now);
        _memberService.Save(member);

        _logger.LogInformation("Generated invitation token for member {MemberId}", memberId);
        return Task.FromResult(token);
    }

    public async Task<InvitationSendResult> SendInvitationAsync(int memberId, string baseUrl)
    {
        var member = _memberService.GetById(memberId);
        if (member == null)
        {
            return new InvitationSendResult
            {
                Success = false,
                Message = $"Member with ID {memberId} not found"
            };
        }

        // Check if already accepted
        var hasAccepted = member.GetValue<bool>("accept2026");
        if (hasAccepted)
        {
            return new InvitationSendResult
            {
                Success = false,
                Message = "Member has already accepted for 2026",
                Email = member.Email
            };
        }

        try
        {
            // Generate or get existing token
            var existingToken = member.GetValue<string>("invitationToken");
            string token;

            if (string.IsNullOrEmpty(existingToken))
            {
                token = await GenerateInvitationTokenAsync(memberId);
            }
            else
            {
                // Update sent date for resend
                member.SetValue("invitationSentDate", DateTime.Now);
                _memberService.Save(member);
                token = existingToken;
            }

            var firstName = member.GetValue<string>("firstName") ?? member.Name?.Split(' ').FirstOrDefault() ?? "Frivillig";
            var invitationUrl = $"{baseUrl.TrimEnd('/')}/umbraco/surface/InvitationSurface/AcceptInvitation?token={token}";

            await _emailService.SendInvitationEmailAsync(member.Email!, firstName, invitationUrl);

            _logger.LogInformation("Sent invitation to member {Email}", member.Email);

            return new InvitationSendResult
            {
                Success = true,
                Message = "Invitation sent successfully",
                Email = member.Email
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send invitation to member {MemberId}", memberId);
            return new InvitationSendResult
            {
                Success = false,
                Message = ex.Message,
                Email = member.Email
            };
        }
    }

    public async Task<BulkInvitationResult> SendBulkInvitationsAsync(string baseUrl)
    {
        var result = new BulkInvitationResult { Success = true };

        var members = _memberService.GetAllMembers().ToList();
        result.TotalMembers = members.Count;

        foreach (var member in members)
        {
            // Skip members who have already accepted
            var hasAccepted = member.GetValue<bool>("accept2026");
            if (hasAccepted)
            {
                result.SkippedCount++;
                continue;
            }

            var sendResult = await SendInvitationAsync(member.Id, baseUrl);
            if (sendResult.Success)
            {
                result.SentCount++;
            }
            else
            {
                result.ErrorCount++;
                result.Errors.Add($"{member.Email}: {sendResult.Message}");
            }
        }

        result.Message = $"Sent {result.SentCount} invitations, skipped {result.SkippedCount}, errors {result.ErrorCount}";
        _logger.LogInformation("Bulk invitation complete: {Message}", result.Message);

        return result;
    }

    public Task<MemberInvitationInfo?> GetMemberByTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult<MemberInvitationInfo?>(null);
        }

        var members = _memberService.GetAllMembers();
        var member = members.FirstOrDefault(m =>
            m.GetValue<string>("invitationToken") == token);

        if (member == null)
        {
            _logger.LogWarning("No member found with invitation token");
            return Task.FromResult<MemberInvitationInfo?>(null);
        }

        var info = new MemberInvitationInfo
        {
            MemberId = member.Id,
            Email = member.Email ?? string.Empty,
            FirstName = member.GetValue<string>("firstName") ?? string.Empty,
            LastName = member.GetValue<string>("lastName") ?? string.Empty,
            Birthdate = member.GetValue<DateTime?>("birthdate")
        };

        return Task.FromResult<MemberInvitationInfo?>(info);
    }

    public async Task<AcceptInvitationResult> AcceptInvitationAsync(string token, IEnumerable<int> crewIds, DateTime birthdate, string password)
    {
        var memberInfo = await GetMemberByTokenAsync(token);
        if (memberInfo == null)
        {
            return new AcceptInvitationResult
            {
                Success = false,
                Message = "Ugyldigt eller udløbet invitationslink."
            };
        }

        var member = _memberService.GetById(memberInfo.MemberId);
        if (member == null)
        {
            return new AcceptInvitationResult
            {
                Success = false,
                Message = "Medlem ikke fundet."
            };
        }

        try
        {
            // Get crew names for confirmation
            var crewIdList = crewIds.ToList();
            var crewNames = new List<string>();

            // Build UDI list for crew wishes content picker
            var udiStrings = new List<string>();
            foreach (var crewId in crewIdList)
            {
                var crew = _contentService.GetById(crewId);
                if (crew != null)
                {
                    crewNames.Add(crew.Name ?? $"Crew {crewId}");
                    var udi = Udi.Create(Constants.UdiEntityType.Document, crew.Key);
                    udiStrings.Add(udi.ToString());
                }
            }

            _logger.LogInformation("Setting crewWishes with {Count} crews: {Udis}", udiStrings.Count, string.Join(",", udiStrings));

            // Set crew wishes (UDI format for multi-content picker)
            if (udiStrings.Any())
            {
                member.SetValue("crewWishes", string.Join(",", udiStrings));
            }

            // Set birthdate
            member.SetValue("birthdate", birthdate);

            // Set acceptance flag and datetime
            member.SetValue("accept2026", true);
            member.SetValue("acceptedDate", DateTime.Now);

            // Clear invitation token (single use)
            member.SetValue("invitationToken", null);

            _memberService.Save(member);

            // Update password using IMemberManager
            var identityMember = await _memberManager.FindByEmailAsync(member.Email!);
            if (identityMember != null)
            {
                var resetToken = await _memberManager.GeneratePasswordResetTokenAsync(identityMember);
                var passwordResult = await _memberManager.ResetPasswordAsync(identityMember, resetToken, password);
                if (!passwordResult.Succeeded)
                {
                    var errors = string.Join(", ", passwordResult.Errors.Select(e => e.Description));
                    _logger.LogWarning("Failed to update password for {Email}: {Errors}", member.Email, errors);
                    return new AcceptInvitationResult
                    {
                        Success = false,
                        Message = $"Kunne ikke opdatere adgangskode: {errors}"
                    };
                }
            }

            // Add to Frivillige member group
            await EnsureMemberInGroupAsync(member);

            // Send confirmation email
            var firstName = member.GetValue<string>("firstName") ?? memberInfo.FirstName;
            await _emailService.SendAcceptanceConfirmationEmailAsync(
                member.Email!,
                firstName,
                crewNames);

            _logger.LogInformation("Member {Email} accepted invitation for 2026 with crews: {Crews}",
                member.Email,
                string.Join(", ", crewNames));

            return new AcceptInvitationResult
            {
                Success = true,
                Message = "Tak for din tilmelding!",
                MemberName = memberInfo.FullName,
                SelectedCrewNames = crewNames
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process invitation acceptance for member {MemberId}", memberInfo.MemberId);
            return new AcceptInvitationResult
            {
                Success = false,
                Message = "Der opstod en fejl. Prøv igen senere."
            };
        }
    }

    public Task<IEnumerable<MemberInvitationStatus>> GetInvitationStatusesAsync()
    {
        var members = _memberService.GetAllMembers();
        var statuses = members.Select(m =>
        {
            var hasToken = !string.IsNullOrEmpty(m.GetValue<string>("invitationToken"));
            var hasAccepted = m.GetValue<bool>("accept2026");

            string status;
            if (hasAccepted)
                status = "Accepted";
            else if (hasToken)
                status = "Invited";
            else
                status = "NotInvited";

            var firstName = m.GetValue<string>("firstName") ?? string.Empty;
            var lastName = m.GetValue<string>("lastName") ?? string.Empty;
            var fullName = $"{firstName} {lastName}".Trim();
            if (string.IsNullOrEmpty(fullName))
                fullName = m.Name ?? m.Email ?? "Unknown";

            return new MemberInvitationStatus
            {
                MemberId = m.Id,
                MemberKey = m.Key,
                Email = m.Email ?? string.Empty,
                FullName = fullName,
                Status = status,
                InvitationSentDate = m.GetValue<DateTime?>("invitationSentDate"),
                AcceptedDate = m.GetValue<DateTime?>("acceptedDate")
            };
        });

        return Task.FromResult(statuses);
    }

    public Task<IEnumerable<CrewInfo>> GetAvailableCrewsAsync()
    {
        var rootContent = _contentService.GetRootContent();
        var crews = new List<CrewInfo>();

        foreach (var root in rootContent)
        {
            FindCrewsRecursive(root, crews);
        }

        _logger.LogDebug("Found {Count} crews", crews.Count);
        return Task.FromResult<IEnumerable<CrewInfo>>(crews);
    }

    private void FindCrewsRecursive(IContent content, List<CrewInfo> crews)
    {
        if (content.ContentType.Alias == CrewContentTypeAlias)
        {
            var description = content.GetValue<string>("description");
            // Strip HTML tags from description if present
            if (!string.IsNullOrEmpty(description))
            {
                description = System.Text.RegularExpressions.Regex.Replace(description, "<[^>]*>", "");
                if (description.Length > 200)
                    description = description[..200] + "...";
            }

            crews.Add(new CrewInfo
            {
                Id = content.Id,
                Key = content.Key,
                Name = content.Name ?? $"Crew {content.Id}",
                Description = description,
                AgeLimit = content.GetValue<int?>("ageLimit")
            });
        }

        // Search children
        var children = _contentService.GetPagedChildren(content.Id, 0, int.MaxValue, out _);
        foreach (var child in children)
        {
            FindCrewsRecursive(child, crews);
        }
    }

    private Task EnsureMemberInGroupAsync(IMember member)
    {
        // Get or create the Frivillige group
        var group = _memberGroupService.GetByName(MemberGroupName);
        if (group == null)
        {
            group = new MemberGroup { Name = MemberGroupName };
            #pragma warning disable CS0618 // Suppress obsolete warning - Save is still functional in v17
            _memberGroupService.Save(group);
            #pragma warning restore CS0618
            _logger.LogInformation("Created member group '{GroupName}'", MemberGroupName);
        }

        // Check if member is already in group
        var memberGroups = _memberService.GetAllRoles(member.Id);
        if (!memberGroups.Contains(MemberGroupName))
        {
            _memberService.AssignRole(member.Id, MemberGroupName);
            _logger.LogInformation("Added member {Email} to group '{GroupName}'", member.Email, MemberGroupName);
        }

        return Task.CompletedTask;
    }
}
