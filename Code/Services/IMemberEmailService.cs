namespace Code.Services;

public interface IMemberEmailService
{
    Task SendPasswordResetEmailAsync(string email, string resetUrl);
    Task SendWelcomeEmailAsync(string email, string firstName, string? memberWish = null, string? timeslotWishes = null);
    Task SendSignupConfirmationEmailAsync(string email, MemberEmailData memberData, IEnumerable<string> selectedCrewNames, string subjectTemplate, string bodyTemplate);
    Task SendInvitationEmailAsync(string email, MemberEmailData memberData, string invitationUrl, string subjectTemplate, string bodyTemplate);
    // [CHANGE: crew invitation feature] Related: Code/Services/CrewInvitationService.cs, Code/Services/MemberEmailService.cs
    Task SendCrewInvitationEmailAsync(string email, string firstName, string crewName, string inviterName, string invitationUrl, string subjectTemplate, string bodyTemplate);
    Task SendAcceptanceConfirmationEmailAsync(string email, MemberEmailData memberData, IEnumerable<string> selectedCrewNames, string subjectTemplate, string bodyTemplate);
    Task SendSupervisorNotificationEmailAsync(string supervisorEmail, string supervisorName, MemberEmailData memberData, string crewName, string subjectTemplate, string bodyTemplate);
    Task SendCrewMessageNotificationAsync(string toEmail, string recipientName, string authorName, string crewName, string messageHtml, string crewUrl);
    Task SendCustomEmailAsync(string email, string subject, string htmlBody, MemberEmailData memberData);
    Task SendCrewAssignmentEmailAsync(string email, MemberEmailData memberData, string crewName, string subjectTemplate, string bodyTemplate);
    Task SendCancelationNotificationAsync(string toEmail, string crewName, string memberFullName, string memberEmail, IEnumerable<string> removedShiftDescriptions);
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
    public string MemberWish { get; set; } = string.Empty;
    public string TimeslotWishes { get; set; } = string.Empty;
    public string PortalUrl { get; set; } = string.Empty;

    // For supervisor notifications
    public string SupervisorName { get; set; } = string.Empty;
    public string CrewName { get; set; } = string.Empty;

    // For custom emails
    public string InvitationUrl { get; set; } = string.Empty;
    public string CurrentCrews { get; set; } = string.Empty;

    // For crew assignment email
    public string SingleTicketUrl { get; set; } = string.Empty;
}
