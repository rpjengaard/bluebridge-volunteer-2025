using System.ComponentModel.DataAnnotations;

namespace Web.ViewModels;

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Email er påkrævet")]
    [EmailAddress(ErrorMessage = "Ugyldig email adresse")]
    [Display(Name = "Email adresse")]
    public string Email { get; set; } = string.Empty;
}
