using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
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
    private readonly IContentService _contentService;
    private readonly IMemberService _memberService;
    private readonly ILogger<MemberAuthSurfaceController> _logger;

    public MemberAuthSurfaceController(
        IUmbracoContextAccessor umbracoContextAccessor,
        IUmbracoDatabaseFactory databaseFactory,
        ServiceContext services,
        AppCaches appCaches,
        IProfilingLogger profilingLogger,
        IPublishedUrlProvider publishedUrlProvider,
        IMemberAuthService authService,
        IMemberEmailService emailService,
        IPublishedContentQuery publishedContentQuery,
        IContentService contentService,
        IMemberService memberService,
        ILogger<MemberAuthSurfaceController> logger)
        : base(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
    {
        _authService = authService;
        _emailService = emailService;
        _publishedContentQuery = publishedContentQuery;
        _contentService = contentService;
        _memberService = memberService;
        _logger = logger;
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
        // Get the referer URL to redirect back to the signup page
        var signupUrl = GetSignupPageUrl();

        if (!ModelState.IsValid)
        {
            TempData["SignupError"] = "Udfyld venligst alle felter korrekt.";
            TempData["SignupModel"] = System.Text.Json.JsonSerializer.Serialize(model);
            return Redirect(signupUrl);
        }

        if (await _authService.MemberExistsAsync(model.Email))
        {
            TempData["SignupError"] = "Der findes allerede en bruger med denne email.";
            TempData["SignupModel"] = System.Text.Json.JsonSerializer.Serialize(model);
            return Redirect(signupUrl);
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
            var errorMessage = string.Join(" ", result.Errors);
            TempData["SignupError"] = errorMessage;
            TempData["SignupModel"] = System.Text.Json.JsonSerializer.Serialize(model);
            return Redirect(signupUrl);
        }

        try
        {
            // Get email templates from site settings
            var (signupSubject, signupBody) = GetSignupEmailTemplates();
            var (supervisorSubject, supervisorBody) = GetSupervisorNotificationTemplates();

            _logger.LogInformation("Signup templates - Subject: {HasSubject}, Body: {HasBody}",
                !string.IsNullOrEmpty(signupSubject), !string.IsNullOrEmpty(signupBody));
            _logger.LogInformation("Supervisor templates - Subject: {HasSubject}, Body: {HasBody}",
                !string.IsNullOrEmpty(supervisorSubject), !string.IsNullOrEmpty(supervisorBody));

            // Get crew names for selected crews
            var crewNames = new List<string>();
            var crewIdsWithSupervisors = new List<(int crewId, string crewName)>();

            foreach (var crewId in model.CrewWishes ?? new List<int>())
            {
                var crew = _contentService.GetById(crewId);
                if (crew != null)
                {
                    crewNames.Add(crew.Name ?? $"Crew {crewId}");
                    crewIdsWithSupervisors.Add((crewId, crew.Name ?? $"Crew {crewId}"));
                }
            }

            _logger.LogInformation("Member selected {CrewCount} crews: {CrewNames}",
                crewNames.Count, string.Join(", ", crewNames));

            // Send signup confirmation email if templates are configured
            if (!string.IsNullOrEmpty(signupSubject) && !string.IsNullOrEmpty(signupBody))
            {
                var portalUrl = $"{Request.Scheme}://{Request.Host}";
                var memberData = new MemberEmailData
                {
                    Email = model.Email,
                    Username = model.Email,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    Phone = model.Phone ?? string.Empty,
                    Zipcode = model.Zipcode ?? string.Empty,
                    PortalUrl = portalUrl
                };

                await _emailService.SendSignupConfirmationEmailAsync(
                    model.Email,
                    memberData,
                    crewNames,
                    signupSubject,
                    signupBody);
            }
            else
            {
                // Fallback to old welcome email if templates not configured
                await _emailService.SendWelcomeEmailAsync(model.Email, model.FirstName);
            }

            // Send supervisor notifications if templates are configured
            if (!string.IsNullOrEmpty(supervisorSubject) && !string.IsNullOrEmpty(supervisorBody))
            {
                _logger.LogInformation("Sending supervisor notifications for {CrewCount} crews", crewIdsWithSupervisors.Count);
                await SendSupervisorNotificationsAsync(
                    model,
                    crewIdsWithSupervisors,
                    crewNames,
                    supervisorSubject,
                    supervisorBody);
            }
            else
            {
                _logger.LogWarning("Supervisor notification templates not configured - skipping notifications");
            }
        }
        catch (Exception ex)
        {
            // Don't fail registration if email fails
            _logger.LogError(ex, "Error sending signup emails for {Email}", model.Email);
        }

        // Check if signup page has a first child article page to redirect to
        var redirectUrl = GetSignupRedirectUrl();
        if (!string.IsNullOrEmpty(redirectUrl))
        {
            return Redirect(redirectUrl);
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

    private string GetSignupPageUrl()
    {
        try
        {
            // Get the signup page from the referer
            var referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer))
            {
                var refererUri = new Uri(referer);
                return refererUri.AbsolutePath;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting signup page URL from referer");
        }

        // Fallback to /signup
        return "/signup";
    }

    private string? GetSignupRedirectUrl()
    {
        try
        {
            // Get the signup page from the referer or current path
            var referer = Request.Headers["Referer"].ToString();
            if (string.IsNullOrEmpty(referer))
            {
                return null;
            }

            var refererUri = new Uri(referer);
            var path = refererUri.AbsolutePath;

            // Get all signup pages and find one matching the path
            var signupPages = _publishedContentQuery.ContentAtRoot()
                .DescendantsOrSelfOfType("bbvSignUp");

            var signupPage = signupPages.FirstOrDefault(x => x.Url() == path);
            if (signupPage != null)
            {
                var firstChild = signupPage.Children?.FirstOrDefault(x => x.ContentType.Alias == "bbvArticlePage");
                if (firstChild != null)
                {
                    _logger.LogInformation("Redirecting to first child article page: {ArticleUrl}", firstChild.Url());
                    return firstChild.Url();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting signup redirect URL");
        }

        return null;
    }

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

    private (string? subject, string? body) GetSignupEmailTemplates()
    {
        var siteSettings = _publishedContentQuery.ContentAtRoot()
            .FirstOrDefault(x => x.ContentType.Alias == "bbvSiteSettings");

        if (siteSettings == null)
        {
            return (null, null);
        }

        var subject = siteSettings.Value<string>("signupEmailEmne");
        var bodyHtml = siteSettings.Value<IHtmlEncodedString>("signupMailText");

        return (subject, bodyHtml?.ToHtmlString());
    }

    private (string? subject, string? body) GetSupervisorNotificationTemplates()
    {
        var siteSettings = _publishedContentQuery.ContentAtRoot()
            .FirstOrDefault(x => x.ContentType.Alias == "bbvSiteSettings");

        if (siteSettings == null)
        {
            return (null, null);
        }

        var subject = siteSettings.Value<string>("supervisorNotificationSubject");
        var bodyHtml = siteSettings.Value<IHtmlEncodedString>("supervisorNotificationTemplate");

        return (subject, bodyHtml?.ToHtmlString());
    }

    private async Task SendSupervisorNotificationsAsync(
        SignupViewModel model,
        List<(int crewId, string crewName)> crews,
        List<string> allCrewNames,
        string subjectTemplate,
        string bodyTemplate)
    {
        var portalUrl = $"{Request.Scheme}://{Request.Host}";
        _logger.LogInformation("Starting supervisor notifications for {CrewCount} crews", crews.Count);

        // Format all selected crews for the template
        var selectedCrewsText = string.Join(", ", allCrewNames);

        foreach (var (crewId, crewName) in crews)
        {
            _logger.LogInformation("Processing crew {CrewId}: {CrewName}", crewId, crewName);

            // Get crew content
            var crewContent = _contentService.GetById(crewId);
            if (crewContent == null)
            {
                _logger.LogWarning("Crew content not found for ID {CrewId}", crewId);
                continue;
            }

            // Get supervisor UDIs from the crew (scheduleSupervisor property)
            var supervisorUdis = crewContent.GetValue<string>("scheduleSupervisor");
            _logger.LogInformation("Crew {CrewName} scheduleSupervisor UDIs: {SupervisorUdis}", crewName, supervisorUdis ?? "(null)");

            if (string.IsNullOrEmpty(supervisorUdis))
            {
                _logger.LogWarning("No scheduleSupervisor configured for crew {CrewName}", crewName);
                continue;
            }

            // Parse UDI strings and get member keys
            var udiList = supervisorUdis.Split(',', StringSplitOptions.RemoveEmptyEntries);
            _logger.LogInformation("Found {UdiCount} supervisor UDIs to process", udiList.Length);

            foreach (var udiString in udiList)
            {
                _logger.LogDebug("Parsing UDI: {UdiString}", udiString.Trim());

                if (!UdiParser.TryParse(udiString.Trim(), out var udi))
                {
                    _logger.LogWarning("Failed to parse UDI: {UdiString}", udiString);
                    continue;
                }

                if (udi is not GuidUdi guidUdi)
                {
                    _logger.LogWarning("UDI is not a GuidUdi: {UdiString}", udiString);
                    continue;
                }

                _logger.LogInformation("Looking up member with GUID: {Guid}", guidUdi.Guid);

                // Get member by key
                var supervisor = _memberService.GetByKey(guidUdi.Guid);
                if (supervisor == null)
                {
                    _logger.LogWarning("Supervisor member not found for GUID: {Guid}", guidUdi.Guid);
                    continue;
                }

                if (string.IsNullOrEmpty(supervisor.Email))
                {
                    _logger.LogWarning("Supervisor {SupervisorName} has no email address", supervisor.Name);
                    continue;
                }

                _logger.LogInformation("Found supervisor: {SupervisorName} ({SupervisorEmail})", supervisor.Name, supervisor.Email);

                // Build member data for template - includes ALL selected crews
                var memberData = new MemberEmailData
                {
                    Email = model.Email,
                    Username = model.Email,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    Phone = model.Phone ?? string.Empty,
                    Zipcode = model.Zipcode ?? string.Empty,
                    SelectedCrews = selectedCrewsText,
                    PortalUrl = portalUrl
                };

                var supervisorFirstName = supervisor.GetValue<string>("firstName") ?? supervisor.Name ?? "Supervisor";

                try
                {
                    _logger.LogInformation("Sending notification to supervisor {SupervisorEmail} for crew {CrewName}",
                        supervisor.Email, crewName);

                    await _emailService.SendSupervisorNotificationEmailAsync(
                        supervisor.Email,
                        supervisorFirstName,
                        memberData,
                        crewName,
                        subjectTemplate,
                        bodyTemplate);

                    _logger.LogInformation("Successfully sent supervisor notification to {SupervisorEmail}", supervisor.Email);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send supervisor notification to {SupervisorEmail}", supervisor.Email);
                }
            }
        }

        _logger.LogInformation("Completed supervisor notifications");
    }
}
