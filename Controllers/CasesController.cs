using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Identity.Web.Resource;
using PricingApi.Domain;
using PricingApi.Dtos;
using PricingApi.Services;

namespace PricingApi.Controllers;

/// <summary>
/// Draft lifecycle:
///   POST   /api/cases        Submit=false  -> creates a Draft (only Title required)
///   PUT    /api/cases/{id}   Submit=false  -> updates the Draft (RowVersion required)
///   PUT    /api/cases/{id}   Submit=true   -> full validation, Draft becomes Open (immutable afterwards)
///   POST   /api/cases        Submit=true   -> create and open in one step
///   GET    /api/cases?status=Draft         -> "my drafts"
///   GET    /api/cases/{id}                 -> load a draft to resume editing
///   DELETE /api/cases/{id}                 -> discard a draft
/// </summary>
[ApiController]
[Authorize]
[RequiredScope("access_as_user")]
[EnableRateLimiting("writes")]
[Route("api/cases")]
public class CasesController(ICaseService cases) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<SaveCaseResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] SaveCaseRequest request, CancellationToken ct)
    {
        var result = await cases.SaveAsync(null, request, CurrentUser.From(User), ct);
        return result.Kind == ResultKind.Ok
            ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value)
            : Fail(result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<SaveCaseResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveCaseRequest request, CancellationToken ct)
    {
        var result = await cases.SaveAsync(id, request, CurrentUser.From(User), ct);
        return result.Kind == ResultKind.Ok ? Ok(result.Value) : Fail(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await cases.GetAsync(id, CurrentUser.From(User), ct);
        return result.Kind == ResultKind.Ok ? Ok(result.Value) : Fail(result);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] CaseStatus? status, CancellationToken ct) =>
        Ok(await cases.ListAsync(CurrentUser.From(User), status, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await cases.DeleteDraftAsync(id, CurrentUser.From(User), ct);
        return result.Kind == ResultKind.Ok ? NoContent() : Fail(result);
    }

    private IActionResult Fail<T>(Result<T> r) => r.Kind switch
    {
        ResultKind.NotFound => NotFound(),
        ResultKind.Conflict => Problem(r.Message, statusCode: StatusCodes.Status409Conflict, title: "Conflict"),
        ResultKind.Invalid => ValidationProblem(new ValidationProblemDetails(r.Errors!)),
        _ => StatusCode(StatusCodes.Status500InternalServerError),
    };
}
