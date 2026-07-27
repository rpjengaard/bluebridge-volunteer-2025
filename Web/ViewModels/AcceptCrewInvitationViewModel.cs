namespace Web.ViewModels;

// [CHANGE: crew invitation feature - shiftadmins invite new volunteers while signup is closed]
// Related: Web/Controllers/CrewInvitationSurfaceController.cs, Web/Views/AcceptCrewInvitation.cshtml

public class AcceptCrewInvitationViewModel
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string CrewName { get; set; } = string.Empty;
    public int? CrewAgeLimit { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime? Birthdate { get; set; }
    public string? Phone { get; set; }
    public string? Zipcode { get; set; }
    public string? Password { get; set; }
    public string? ConfirmPassword { get; set; }
}
