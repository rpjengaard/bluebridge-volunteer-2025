using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Security;

namespace Code.Services;

public class MemberAuthService : IMemberAuthService
{
    private readonly IMemberManager _memberManager;
    private readonly IMemberSignInManager _memberSignInManager;
    private readonly IMemberService _memberService;
    private readonly ILogger<MemberAuthService> _logger;

    public MemberAuthService(
        IMemberManager memberManager,
        IMemberSignInManager memberSignInManager,
        IMemberService memberService,
        ILogger<MemberAuthService> logger)
    {
        _memberManager = memberManager;
        _memberSignInManager = memberSignInManager;
        _memberService = memberService;
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
        DateTime? birthdate)
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

            if (!string.IsNullOrEmpty(phone))
                member.SetValue("phone", phone);

            if (birthdate.HasValue)
                member.SetValue("birthdate", birthdate.Value);

            // Set acceptance properties
            member.SetValue("accept2026", true);
            member.SetValue("acceptedDate", DateTime.UtcNow);

            _memberService.Save(member);
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
