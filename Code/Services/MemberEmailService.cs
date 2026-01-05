using Microsoft.Extensions.Logging;

namespace Code.Services;

public class MemberEmailService : IMemberEmailService
{
    private readonly ILogger<MemberEmailService> _logger;

    public MemberEmailService(ILogger<MemberEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendPasswordResetEmailAsync(string email, string resetUrl)
    {
        _logger.LogInformation(
            "[MOCK EMAIL] Password reset email to {Email}\n" +
            "Subject: Blue Bridge - Nulstil din adgangskode\n" +
            "Reset URL: {ResetUrl}",
            email,
            resetUrl);

        return Task.CompletedTask;
    }

    public Task SendWelcomeEmailAsync(string email, string firstName)
    {
        _logger.LogInformation(
            "[MOCK EMAIL] Welcome email to {Email}\n" +
            "Subject: Velkommen til Blue Bridge Portal\n" +
            "Name: {FirstName}",
            email,
            firstName);

        return Task.CompletedTask;
    }

    public Task SendInvitationEmailAsync(string email, string firstName, string invitationUrl)
    {
        _logger.LogInformation(
            "[MOCK EMAIL] Invitation email to {Email} for {FirstName}. " +
            "Subject: Blue Bridge 2026 - Du er inviteret til at blive frivillig igen! " +
            "Invitation URL: {InvitationUrl}",
            email,
            firstName,
            invitationUrl);

        return Task.CompletedTask;
    }

    public Task SendAcceptanceConfirmationEmailAsync(string email, string firstName, IEnumerable<string> selectedCrewNames)
    {
        var crewList = string.Join(", ", selectedCrewNames);

        _logger.LogInformation(
            "[MOCK EMAIL] Acceptance confirmation email to {Email} for {FirstName}. " +
            "Subject: Tak for din tilmelding til Blue Bridge 2026! " +
            "Selected Crews: {CrewList}",
            email,
            firstName,
            crewList);

        return Task.CompletedTask;
    }

    public Task<bool> SendJobApplicationAcceptedEmailAsync(string email, string memberName, string jobTitle, string crewName, string ticketLink)
    {
        _logger.LogInformation(
            "[MOCK EMAIL] Job application accepted email to {Email}\n" +
            "Subject: Din ansøgning til {JobTitle} hos {CrewName} er godkendt!\n" +
            "Member: {MemberName}\n" +
            "Job: {JobTitle}\n" +
            "Crew: {CrewName}\n" +
            "Ticket Link: {TicketLink}\n" +
            "\n" +
            "Tillykke! Din ansøgning er blevet godkendt.\n" +
            "Klik på linket nedenfor for at købe din billet:\n" +
            "{TicketLink}",
            email,
            jobTitle,
            crewName,
            memberName,
            ticketLink);

        return Task.FromResult(true);
    }
}
