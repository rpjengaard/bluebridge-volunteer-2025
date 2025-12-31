namespace Code.Services;

public interface IMemberEmailService
{
    Task SendPasswordResetEmailAsync(string email, string resetUrl);
    Task SendWelcomeEmailAsync(string email, string firstName);
    Task SendInvitationEmailAsync(string email, string firstName, string invitationUrl);
    Task SendAcceptanceConfirmationEmailAsync(string email, string firstName, IEnumerable<string> selectedCrewNames);
}
