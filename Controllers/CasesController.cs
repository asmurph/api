using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Identity.Web.Resource;
using PricingApi.Domain;
using PricingApi.Dtos;
using PricingApi.Services;

namespace PricingApi.Controllers;

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

    [HttpGet("{id:guid}/versions")]
    [ProducesResponseType<IReadOnlyList<CaseVersionSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListVersions(Guid id, CancellationToken ct)
    {
        var result = await cases.ListVersionsAsync(id, CurrentUser.From(User), ct);
        return result.Kind == ResultKind.Ok ? Ok(result.Value) : Fail(result);
    }

    [HttpGet("{id:guid}/versions/{revision:int:min(1)}")]
    [ProducesResponseType<CaseVersionDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVersion(Guid id, int revision, CancellationToken ct)
    {
        var result = await cases.GetVersionAsync(id, revision, CurrentUser.From(User), ct);
        return result.Kind == ResultKind.Ok ? Ok(result.Value) : Fail(result);
    }

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
