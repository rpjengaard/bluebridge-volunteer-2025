using System.ComponentModel.DataAnnotations;

namespace Web.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Email er påkrævet")]
    [EmailAddress(ErrorMessage = "Ugyldig email adresse")]
    [Display(Name = "Email adresse")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Adgangskode er påkrævet")]
    [DataType(DataType.Password)]
    [Display(Name = "Adgangskode")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Husk mig")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}
