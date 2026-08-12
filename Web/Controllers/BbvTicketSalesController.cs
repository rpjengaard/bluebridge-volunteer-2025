// [CHANGE: SuperAdmin ticket sales page] Related: Code/Services/SuperAdminService.cs, Web/Controllers/TicketSalesApiController.cs, Web/Views/TicketSales.cshtml
using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;

namespace Web.Controllers;

/// <summary>
/// Render controller for the bbvTicketSales content type (frontend "Billetsalg"
/// page). Only members in the SuperAdmin member group may view the page:
/// anonymous visitors are sent to login, other members to the frontpage.
/// The sales data itself is loaded client-side from TicketSalesApiController.
/// </summary>
public class BbvTicketSalesController : RenderController
{
    private readonly IMemberManager _memberManager;
    private readonly ISuperAdminService _superAdminService;

    public BbvTicketSalesController(
        ILogger<BbvTicketSalesController> logger,
        ICompositeViewEngine compositeViewEngine,
        IUmbracoContextAccessor umbracoContextAccessor,
        IMemberManager memberManager,
        ISuperAdminService superAdminService)
        : base(logger, compositeViewEngine, umbracoContextAccessor)
    {
        _memberManager = memberManager;
        _superAdminService = superAdminService;
    }

    public override IActionResult Index()
    {
        return IndexAsync().GetAwaiter().GetResult();
    }

    private async Task<IActionResult> IndexAsync()
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
            return Redirect($"/login?returnUrl={Uri.EscapeDataString(HttpContext.Request.Path)}");

        // [CHANGE: review fix — reuse the already loaded member instead of a second lookup] Related: Code/Services/SuperAdminService.cs
        if (!await _superAdminService.IsSuperAdminAsync(currentMember))
            return Redirect("/");

        return View("~/Views/TicketSales.cshtml", CurrentPage);
    }
}
