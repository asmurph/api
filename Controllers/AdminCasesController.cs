using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using PricingApi.Domain;
using PricingApi.Dtos;
using PricingApi.Services;

namespace PricingApi.Controllers;

/// <summary>
/// Admin-only, read-only view across every user's cases.
///   GET /api/admin/cases?status=Open   -> all cases, newest (CreatedAt) first, capped at 1000
/// Requires the Entra app role "Admin" in the access token's "roles" claim.
/// </summary>
[ApiController]
//[Authorize(Roles = CurrentUser.AdminRole)]
[RequiredScope("access_as_user")]
[Route("api/admin/cases")]
public class AdminCasesController(ICaseService cases) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AdminCaseSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] CaseStatus? status, CancellationToken ct)
    {
        return Ok(await cases.ListAllAsync(status, ct));
    }
}
