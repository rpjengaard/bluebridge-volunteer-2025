namespace Code.Services;

public interface IInvitationService
{
    Task<string> GenerateInvitationTokenAsync(int memberId);
    Task<InvitationSendResult> SendInvitationAsync(int memberId, string baseUrl);
    Task<BulkInvitationResult> SendBulkInvitationsAsync(string baseUrl);
    Task<MemberInvitationInfo?> GetMemberByTokenAsync(string token);
    Task<AcceptInvitationResult> AcceptInvitationAsync(string token, IEnumerable<int> crewIds, DateTime birthdate, string password);
    Task<IEnumerable<MemberInvitationStatus>> GetInvitationStatusesAsync();
    Task<IEnumerable<CrewInfo>> GetAvailableCrewsAsync();
}

public class InvitationSendResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Email { get; set; }
}

public class BulkInvitationResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int TotalMembers { get; set; }
    public int SentCount { get; set; }
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class MemberInvitationInfo
{
    public int MemberId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime? Birthdate { get; set; }
    public string FullName => $"{FirstName} {LastName}".Trim();
}

public class AcceptInvitationResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? MemberName { get; set; }
    public IEnumerable<string>? SelectedCrewNames { get; set; }
}

public class MemberInvitationStatus
{
    public int MemberId { get; set; }
    public Guid MemberKey { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Status { get; set; } = "NotInvited"; // NotInvited, Invited, Accepted
    public DateTime? InvitationSentDate { get; set; }
    public DateTime? AcceptedDate { get; set; }
}

public class CrewInfo
{
    public int Id { get; set; }
    public Guid Key { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? AgeLimit { get; set; }
}
