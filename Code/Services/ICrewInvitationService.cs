namespace Code.Services;

// [CHANGE: crew invitation feature - shiftadmins invite new volunteers while signup is closed]
// Related: Code/Services/CrewInvitationService.cs, Code/Migrations/AddCrewInvitationTableMigration.cs, Web/Controllers/CrewInvitationSurfaceController.cs

public interface ICrewInvitationService
{
    /// <summary>
    /// Sends a personal crew invitation. If the email belongs to an existing member who has not
    /// accepted 2026, the existing member-invitation flow is used instead. Active members are rejected.
    /// </summary>
    Task<CrewInvitationSendResult> SendInvitationAsync(int crewId, string email, string firstName, string lastName, string inviterEmail, string baseUrl);

    Task<List<CrewInvitationItem>> GetInvitationsForCrewAsync(int crewId);

    Task<CrewInvitationSendResult> ResendInvitationAsync(int invitationId, string baseUrl);

    Task<bool> CancelInvitationAsync(int invitationId);

    /// <summary>Returns invitation info for a valid (pending, non-expired) token; null otherwise.</summary>
    Task<CrewInvitationInfo?> GetByTokenAsync(string token);

    /// <summary>Creates the member, assigns the crew directly, marks the invite accepted and signs the member in.</summary>
    Task<CrewInvitationAcceptResult> AcceptInvitationAsync(CrewInvitationAcceptRequest request);
}

public class CrewInvitationSendResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class CrewInvitationItem
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string InvitedByName { get; set; } = string.Empty;
    public DateTime SentDate { get; set; }
    public DateTime? AcceptedDate { get; set; }
    /// <summary>Pending, Accepted, Expired or Canceled</summary>
    public string Status { get; set; } = "Pending";
}

public class CrewInvitationInfo
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public int CrewId { get; set; }
    public string CrewName { get; set; } = string.Empty;
    public int? CrewAgeLimit { get; set; }
}

public class CrewInvitationAcceptRequest
{
    public string Token { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime Birthdate { get; set; }
    public string? Phone { get; set; }
    public string? Zipcode { get; set; }
    public string Password { get; set; } = string.Empty;
    public string PortalUrl { get; set; } = string.Empty;
}

public class CrewInvitationAcceptResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? MemberName { get; set; }
    public string? CrewName { get; set; }
}
