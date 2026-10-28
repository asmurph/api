using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.Resource;

namespace EntraApi.Controllers;

[ApiController]
[Authorize]
[RequiredScope("access_as_user")]   // token's "scp" claim must contain this delegated scope
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        message = "Token validated successfully.",
        name = User.FindFirst("name")?.Value,
        username = User.FindFirst("preferred_username")?.Value,
        objectId = User.GetObjectId(),
        tenantId = User.GetTenantId(),
        scopes = User.FindFirst("scp")?.Value
    });
}
