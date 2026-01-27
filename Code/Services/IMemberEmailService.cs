namespace Code.Services;

public interface IMemberEmailService
{
    Task SendPasswordResetEmailAsync(string email, string resetUrl);
    Task SendWelcomeEmailAsync(string email, string firstName);
    Task SendSignupConfirmationEmailAsync(string email, MemberEmailData memberData, IEnumerable<string> selectedCrewNames, string subjectTemplate, string bodyTemplate);
    Task SendInvitationEmailAsync(string email, MemberEmailData memberData, string invitationUrl, string subjectTemplate, string bodyTemplate);
    Task SendAcceptanceConfirmationEmailAsync(string email, MemberEmailData memberData, IEnumerable<string> selectedCrewNames, string subjectTemplate, string bodyTemplate);
    Task SendSupervisorNotificationEmailAsync(string supervisorEmail, string supervisorName, MemberEmailData memberData, string crewName, string subjectTemplate, string bodyTemplate);
}

public class MemberEmailData
{
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Zipcode { get; set; } = string.Empty;
    public string TidligereArbejdssteder { get; set; } = string.Empty;
    public string SelectedCrews { get; set; } = string.Empty;
    public string PortalUrl { get; set; } = string.Empty;

    // For supervisor notifications
    public string SupervisorName { get; set; } = string.Empty;
    public string CrewName { get; set; } = string.Empty;
}
