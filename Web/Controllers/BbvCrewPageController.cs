using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;

namespace Web.Controllers;

/// <summary>
/// Custom render controller for BbvCrewPage content type.
/// When the URL contains ?vagtskemaer it renders the dedicated
/// schedule planner view (Views/CrewSchedule.cshtml) instead of
/// the default Crew view.
/// </summary>
public class BbvCrewPageController : RenderController
{
    public BbvCrewPageController(
        ILogger<BbvCrewPageController> logger,
        ICompositeViewEngine compositeViewEngine,
        IUmbracoContextAccessor umbracoContextAccessor)
        : base(logger, compositeViewEngine, umbracoContextAccessor)
    {
    }

    public override IActionResult Index()
    {
        if (HttpContext.Request.Query.ContainsKey("vagtskemaer"))
            return View("CrewSchedule", CurrentPage);

        return base.Index();
    }
}
