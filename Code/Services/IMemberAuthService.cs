using Microsoft.AspNetCore.Identity;

namespace Code.Services;

public record LoginResult(bool Succeeded, bool IsLockedOut, bool IsNotAllowed, string? ErrorMessage = null);
public record SignupResult(bool Succeeded, IEnumerable<string> Errors);
public record PasswordResetResult(bool Succeeded, IEnumerable<string> Errors);

public interface IMemberAuthService
{
    Task<LoginResult> LoginAsync(string email, string password, bool rememberMe);
    Task LogoutAsync();
    Task<SignupResult> SignupAsync(string email, string password, string firstName, string lastName, string? phone, DateTime? birthdate);
    Task<bool> MemberExistsAsync(string email);
    Task<string?> GeneratePasswordResetTokenAsync(string email);
    Task<PasswordResetResult> ResetPasswordAsync(string email, string token, string newPassword);
}
