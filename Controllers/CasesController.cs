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
public class CasesController : ControllerBase
{
    private readonly ILogger<CasesController> _logger;
    private readonly ICaseService _cases;

    public CasesController(ICaseService cases, ILogger<CasesController> logger)
    {
        _cases = cases;
        _logger = logger;
    }

    // Explicit draft create endpoint.
    [HttpPost("drafts")]
    [ProducesResponseType<SaveCaseResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateDraft([FromBody] SaveCaseRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Current User Saved Draft{CurrentUser.From(User)}",CurrentUser.From(User));
        request.Submit = false;
        var result = await _cases.SaveAsync(null, request, CurrentUser.From(User), ct);
        return result.Kind == ResultKind.Ok
            ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value)
            : Fail(result);
    }

    // Submit a draft as a real/open case.
    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType<SaveCaseResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit(Guid id, [FromBody] SaveCaseRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Current User Submitted Case{CurrentUser.From(User)}", CurrentUser.From(User));
        request.Submit = true;
        var result = await _cases.SaveAsync(id, request, CurrentUser.From(User), ct);
        return result.Kind == ResultKind.Ok ? Ok(result.Value) : Fail(result);
    }

    // Backward-compatible create endpoint.
    [HttpPost]
    [ProducesResponseType<SaveCaseResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] SaveCaseRequest request, CancellationToken ct)
    {
        var result = await _cases.SaveAsync(null, request, CurrentUser.From(User), ct);
        return result.Kind == ResultKind.Ok
            ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value)
            : Fail(result);
    }

    // Update draft OR open case; version snapshot is created in service.
    [HttpPut("{id:guid}")]
    [ProducesResponseType<SaveCaseResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveCaseRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Current User Updated Draft Case{CurrentUser.From(User)}", CurrentUser.From(User));
        var result = await _cases.SaveAsync(id, request, CurrentUser.From(User), ct);
        return result.Kind == ResultKind.Ok ? Ok(result.Value) : Fail(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _cases.GetAsync(id, CurrentUser.From(User), ct);
        return result.Kind == ResultKind.Ok ? Ok(result.Value) : Fail(result);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] CaseStatus? status, CancellationToken ct) =>
        Ok(await _cases.ListAsync(CurrentUser.From(User), status, ct));

    [HttpGet("{id:guid}/versions")]
    [ProducesResponseType<IReadOnlyList<CaseVersionSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListVersions(Guid id, CancellationToken ct)
    {
        var result = await _cases.ListVersionsAsync(id, CurrentUser.From(User), ct);
        return result.Kind == ResultKind.Ok ? Ok(result.Value) : Fail(result);
    }

    [HttpGet("{id:guid}/versions/{revision:int:min(1)}")]
    [ProducesResponseType<CaseVersionDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVersion(Guid id, int revision, CancellationToken ct)
    {
        var result = await _cases.GetVersionAsync(id, revision, CurrentUser.From(User), ct);
        return result.Kind == ResultKind.Ok ? Ok(result.Value) : Fail(result);
    }

    [HttpPost("{id:guid}/files/publish")]
    [ProducesResponseType<PublishCaseFilesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PublishFiles(Guid id, CancellationToken ct)
    {
        var result = await _cases.PublishFilesAsync(id, CurrentUser.From(User), ct);
        return result.Kind == ResultKind.Ok ? Ok(result.Value) : Fail(result);
    }

    [HttpGet("{id:guid}/files/versions")]
    [ProducesResponseType<IReadOnlyList<SharePointFileVersionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFileVersions(Guid id, CancellationToken ct)
    {
        var result = await _cases.ListFileVersionsAsync(id, CurrentUser.From(User), ct);
        return result.Kind == ResultKind.Ok ? Ok(result.Value) : Fail(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _cases.DeleteDraftAsync(id, CurrentUser.From(User), ct);
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
