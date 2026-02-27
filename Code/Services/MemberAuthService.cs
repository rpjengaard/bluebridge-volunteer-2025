using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Security;

namespace Code.Services;

public class MemberAuthService : IMemberAuthService
{
    private readonly IMemberManager _memberManager;
    private readonly IMemberSignInManager _memberSignInManager;
    private readonly IMemberService _memberService;
    private readonly IMemberGroupService _memberGroupService;
    private readonly IContentService _contentService;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly ILogger<MemberAuthService> _logger;

    private const string MemberGroupName = "Frivillige";

    public MemberAuthService(
        IMemberManager memberManager,
        IMemberSignInManager memberSignInManager,
        IMemberService memberService,
        IMemberGroupService memberGroupService,
        IContentService contentService,
        IJsonSerializer jsonSerializer,
        ILogger<MemberAuthService> logger)
    {
        _memberManager = memberManager;
        _memberSignInManager = memberSignInManager;
        _memberService = memberService;
        _memberGroupService = memberGroupService;
        _contentService = contentService;
        _jsonSerializer = jsonSerializer;
        _logger = logger;
    }

    public async Task<LoginResult> LoginAsync(string email, string password, bool rememberMe)
    {
        var result = await _memberSignInManager.PasswordSignInAsync(
            email,
            password,
            rememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            _logger.LogInformation("Member {Email} logged in successfully", email);
            return new LoginResult(true, false, false);
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("Member {Email} is locked out", email);
            return new LoginResult(false, true, false, "Din konto er låst. Prøv igen senere.");
        }

        if (result.IsNotAllowed)
        {
            _logger.LogWarning("Member {Email} login not allowed", email);
            return new LoginResult(false, false, true, "Login er ikke tilladt. Kontakt administrator.");
        }

        _logger.LogWarning("Failed login attempt for {Email}", email);
        return new LoginResult(false, false, false, "Ugyldig email eller adgangskode.");
    }

    public async Task LogoutAsync()
    {
        await _memberSignInManager.SignOutAsync();
        _logger.LogInformation("Member logged out");
    }

    public async Task<SignupResult> SignupAsync(
        string email,
        string password,
        string firstName,
        string lastName,
        string? phone,
        DateTime? birthdate,
        string? zipcode,
        List<int>? crewWishes,
        string? memberWish,
        List<string>? selectedTimeslots)
    {
        var memberName = $"{firstName} {lastName}";

        var identityUser = MemberIdentityUser.CreateNew(
            email,
            email,
            "bbvMember",
            isApproved: true,
            memberName);

        var createResult = await _memberManager.CreateAsync(identityUser, password);

        if (!createResult.Succeeded)
        {
            var errors = createResult.Errors.Select(e => TranslateIdentityError(e.Code));
            _logger.LogWarning("Failed to create member {Email}: {Errors}", email, string.Join(", ", errors));
            return new SignupResult(false, errors);
        }

        // Set custom member properties
        var member = _memberService.GetByEmail(email);
        if (member != null)
        {
            member.SetValue("firstName", firstName);
            member.SetValue("lastName", lastName);
            member.SetValue("accept2026", true);
            member.SetValue("acceptedDate", DateTime.UtcNow);

            if (!string.IsNullOrEmpty(phone))
                member.SetValue("phone", phone);

            if (birthdate.HasValue)
                member.SetValue("birthdate", birthdate.Value);

            if (!string.IsNullOrEmpty(zipcode))
                member.SetValue("zipcode", zipcode);

            if (crewWishes != null && crewWishes.Count > 0)
            {
                // Convert crew IDs to UDI format for Umbraco content picker
                var crewUdis = new List<string>();
                foreach (var crewId in crewWishes)
                {
                    var content = _contentService.GetById(crewId);
                    if (content != null)
                    {
                        crewUdis.Add($"umb://document/{content.Key:N}");
                    }
                }

                if (crewUdis.Count > 0)
                {
                    member.SetValue("crewWishes", string.Join(",", crewUdis));
                }
            }

            if (!string.IsNullOrWhiteSpace(memberWish))
                member.SetValue("memberWish", memberWish);

            if (selectedTimeslots != null && selectedTimeslots.Count > 0)
            {
                var timeslotArray = selectedTimeslots.ToArray();
                member.SetValue("timeslotWish", _jsonSerializer.Serialize(timeslotArray));
            }

            _memberService.Save(member);
            EnsureMemberInGroup(member);
        }

        // Auto-login after registration
        await _memberSignInManager.SignInAsync(identityUser, isPersistent: false);

        _logger.LogInformation("Member {Email} created and signed in successfully", email);
        return new SignupResult(true, Enumerable.Empty<string>());
    }

    public async Task<bool> MemberExistsAsync(string email)
    {
        var member = await _memberManager.FindByEmailAsync(email);
        return member != null;
    }

    public async Task<string?> GeneratePasswordResetTokenAsync(string email)
    {
        var member = await _memberManager.FindByEmailAsync(email);
        if (member == null)
        {
            _logger.LogWarning("Password reset requested for non-existent email {Email}", email);
            return null;
        }

        var token = await _memberManager.GeneratePasswordResetTokenAsync(member);
        _logger.LogInformation("Password reset token generated for {Email}", email);
        return token;
    }

    public async Task<PasswordResetResult> ResetPasswordAsync(string email, string token, string newPassword)
    {
        var member = await _memberManager.FindByEmailAsync(email);
        if (member == null)
        {
            return new PasswordResetResult(false, new[] { "Ugyldigt link. Anmod om et nyt nulstillingslink." });
        }

        var result = await _memberManager.ResetPasswordAsync(member, token, newPassword);

        if (result.Succeeded)
        {
            _logger.LogInformation("Password reset successful for {Email}", email);
            return new PasswordResetResult(true, Enumerable.Empty<string>());
        }

        var errors = result.Errors.Select(e => TranslateIdentityError(e.Code));
        _logger.LogWarning("Password reset failed for {Email}: {Errors}", email, string.Join(", ", errors));
        return new PasswordResetResult(false, errors);
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
            "InvalidToken" => "Ugyldigt eller udløbet link. Anmod om et nyt.",
            _ => "Der opstod en fejl. Prøv igen."
        };
    }
}
