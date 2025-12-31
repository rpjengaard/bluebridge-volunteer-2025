using System.ComponentModel.DataAnnotations;

namespace Web.ViewModels;

public class SignupViewModel
{
    [Required(ErrorMessage = "Fornavn er påkrævet")]
    [StringLength(100, ErrorMessage = "Fornavn må max være 100 tegn")]
    [Display(Name = "Fornavn")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Efternavn er påkrævet")]
    [StringLength(100, ErrorMessage = "Efternavn må max være 100 tegn")]
    [Display(Name = "Efternavn")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email er påkrævet")]
    [EmailAddress(ErrorMessage = "Ugyldig email adresse")]
    [Display(Name = "Email adresse")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Ugyldigt telefonnummer")]
    [Display(Name = "Telefon")]
    public string? Phone { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Fødselsdato")]
    public DateTime? Birthdate { get; set; }

    [Required(ErrorMessage = "Adgangskode er påkrævet")]
    [StringLength(100, MinimumLength = 10, ErrorMessage = "Adgangskode skal være mindst 10 tegn")]
    [DataType(DataType.Password)]
    [Display(Name = "Adgangskode")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bekræft adgangskode")]
    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "Adgangskoderne matcher ikke")]
    [Display(Name = "Bekræft adgangskode")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Range(typeof(bool), "true", "true", ErrorMessage = "Du skal acceptere vilkårene")]
    [Display(Name = "Jeg accepterer vilkårene")]
    public bool AcceptTerms { get; set; }
}
