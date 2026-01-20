using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Website.Controllers;
using Umbraco.Extensions;
using Web.ViewModels;

namespace Web.Controllers;

public class MemberAuthSurfaceController : SurfaceController
{
    private readonly IMemberAuthService _authService;
    private readonly IMemberEmailService _emailService;
    private readonly IPublishedContentQuery _publishedContentQuery;

    public MemberAuthSurfaceController(
        IUmbracoContextAccessor umbracoContextAccessor,
        IUmbracoDatabaseFactory databaseFactory,
        ServiceContext services,
        AppCaches appCaches,
        IProfilingLogger profilingLogger,
        IPublishedUrlProvider publishedUrlProvider,
        IMemberAuthService authService,
        IMemberEmailService emailService,
        IPublishedContentQuery publishedContentQuery)
        : base(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
    {
        _authService = authService;
        _emailService = emailService;
        _publishedContentQuery = publishedContentQuery;
    }

    #region Login

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HandleLogin(LoginViewModel model)
    {
        var returnUrl = model.ReturnUrl;
        var loginRedirect = string.IsNullOrEmpty(returnUrl)
            ? "/login"
            : $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}";

        if (!ModelState.IsValid)
        {
            TempData["LoginError"] = "Udfyld venligst alle felter korrekt.";
            return Redirect(loginRedirect);
        }

        var result = await _authService.LoginAsync(model.Email, model.Password, model.RememberMe);

        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            var dashboardUrl = GetDashboardUrl();
            return Redirect(dashboardUrl ?? "/");
        }

        TempData["LoginError"] = result.ErrorMessage ?? "Der opstod en fejl.";
        return Redirect(loginRedirect);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HandleLogout()
    {
        await _authService.LogoutAsync();
        return Redirect("/");
    }

    #endregion

    #region Signup

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HandleSignup(SignupViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["SignupError"] = "Udfyld venligst alle felter korrekt.";
            return Redirect("/signup");
        }

        if (await _authService.MemberExistsAsync(model.Email))
        {
            TempData["SignupError"] = "Der findes allerede en bruger med denne email.";
            return Redirect("/signup");
        }

        var result = await _authService.SignupAsync(
            model.Email,
            model.Password,
            model.FirstName,
            model.LastName,
            model.Phone,
            model.Birthdate,
            model.Zipcode,
            model.CrewWishes);

        if (!result.Succeeded)
        {
            TempData["SignupError"] = string.Join(" ", result.Errors);
            return Redirect("/signup");
        }

        try
        {
            await _emailService.SendWelcomeEmailAsync(model.Email, model.FirstName);
        }
        catch
        {
            // Don't fail registration if email fails
        }

        var dashboardUrl = GetDashboardUrl();
        return Redirect(dashboardUrl ?? "/");
    }

    #endregion

    #region Forgot Password

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HandleForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return Redirect("/login?forgot=true");
        }

        // Always show success to prevent email enumeration
        TempData["ForgotPasswordSuccess"] = true;

        var token = await _authService.GeneratePasswordResetTokenAsync(model.Email);
        if (token != null)
        {
            var resetUrl = Url.Action(
                "ResetPassword",
                "MemberAuthSurface",
                new { email = model.Email, token = token },
                Request.Scheme);

            try
            {
                await _emailService.SendPasswordResetEmailAsync(model.Email, resetUrl!);
            }
            catch
            {
                // Don't expose email errors
            }
        }

        return Redirect("/login?forgot=true");
    }

    [HttpGet]
    public IActionResult ResetPassword(string email, string token)
    {
        var model = new ResetPasswordViewModel
        {
            Email = email,
            Token = token
        };

        return View("~/Views/ResetPassword.cshtml", model);
    }

    [HttpPost]
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

    [HttpGet]
    public IActionResult ResetPasswordConfirmation()
    {
        return View("~/Views/ResetPasswordConfirmation.cshtml");
    }

    #endregion

    private string? GetDashboardUrl()
    {
        // Get the login frontpage URL from site settings (using published content cache)
        var siteSettings = _publishedContentQuery.ContentAtRoot()
            .FirstOrDefault(x => x.ContentType.Alias == "bbvSiteSettings");

        if (siteSettings != null)
        {
            var loginFrontpage = siteSettings.Value<Umbraco.Cms.Core.Models.PublishedContent.IPublishedContent>("loginFrontpage");
            if (loginFrontpage != null)
            {
                return loginFrontpage.Url();
            }
        }

        // Fallback to dashboard
        return "/dashboard";
    }
}
