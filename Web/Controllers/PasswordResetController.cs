using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Web.ViewModels;

namespace Web.Controllers;

[Route("reset-password")]
public class PasswordResetController : Controller
{
    private readonly IMemberAuthService _authService;
    private readonly ILogger<PasswordResetController> _logger;

    public PasswordResetController(
        IMemberAuthService authService,
        ILogger<PasswordResetController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpGet("")]
    public IActionResult ResetPassword([FromQuery] string email, [FromQuery] string token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            return Redirect("/login");
        }

        var model = new ResetPasswordViewModel
        {
            Email = email,
            Token = token
        };

        return View("~/Views/ResetPassword.cshtml", model);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HandleResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("~/Views/ResetPassword.cshtml", model);
        }

        var result = await _authService.ResetPasswordAsync(model.Email, model.Token, model.NewPassword);

        if (result.Succeeded)
        {
            TempData["ResetPasswordSuccess"] = true;
            return RedirectToAction("ResetPasswordConfirmation");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }

        return View("~/Views/ResetPassword.cshtml", model);
    }

    [HttpGet("confirmation")]
    public IActionResult ResetPasswordConfirmation()
    {
        return View("~/Views/ResetPasswordConfirmation.cshtml");
    }
}
