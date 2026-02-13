using System.ComponentModel.DataAnnotations;

namespace Web.ViewModels;

public class AcceptInvitationViewModel
{
    [Required]
    public string Token { get; set; } = string.Empty;

    public string MemberName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public DateTime? CurrentBirthdate { get; set; }

    [Required(ErrorMessage = "Fødselsdato er påkrævet.")]
    public DateTime? Birthdate { get; set; }

    [Required(ErrorMessage = "Adgangskode er påkrævet.")]
    [MinLength(10, ErrorMessage = "Adgangskoden skal være mindst 10 tegn.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bekræft adgangskode er påkrævet.")]
    [Compare("Password", ErrorMessage = "Adgangskoderne matcher ikke.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public List<CrewSelectionItem> AvailableCrews { get; set; } = new();

    public List<int> SelectedCrewIds { get; set; } = new();

    public string? MemberWish { get; set; }

    public List<string> SelectedTimeslots { get; set; } = new();

    public List<TimeslotOption> AvailableTimeslots { get; set; } = new();
}

public class CrewSelectionItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? AgeLimit { get; set; }
}

public class TimeslotOption
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
