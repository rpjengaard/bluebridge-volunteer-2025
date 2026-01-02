using Microsoft.AspNetCore.Mvc;

namespace Web.Controllers;

public class MemberController : Controller
{
    [HttpGet("/member")]
    public IActionResult Index()
    {
        return View("~/Views/Member.cshtml");
    }
}
