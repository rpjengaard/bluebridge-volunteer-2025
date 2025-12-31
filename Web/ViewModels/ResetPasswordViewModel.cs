using System.ComponentModel.DataAnnotations;

namespace Web.ViewModels;

public class ResetPasswordViewModel
{
    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ny adgangskode er påkrævet")]
    [StringLength(100, MinimumLength = 10, ErrorMessage = "Adgangskode skal være mindst 10 tegn")]
    [DataType(DataType.Password)]
    [Display(Name = "Ny adgangskode")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bekræft adgangskode")]
    [DataType(DataType.Password)]
    [Compare("NewPassword", ErrorMessage = "Adgangskoderne matcher ikke")]
    [Display(Name = "Bekræft ny adgangskode")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
