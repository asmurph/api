using System.ComponentModel.DataAnnotations;

namespace PricingApi.Dtos;

public class RoutingDto
{
    public bool ClinicalJudgment { get; set; }
    public bool NovelTechnology { get; set; }
    public bool MethodologyChanges { get; set; }
    [StringLength(2000)] public string? Rationale { get; set; }
}

public class WorkPortionDto
{
    public int Sequence { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    public string? Team { get; set; }
    public DateOnly? DueDate { get; set; }
}

/// <summary>
/// Body for both POST /api/cases (create) and PUT /api/cases/{id} (update).
/// Submit=false saves a draft (only Title is required); Submit=true validates everything and opens the case.
/// Server-controlled values (status, owner, case number, routing flags) are never read from the client.
/// </summary>
public class SaveCaseRequest
{
    public bool Submit { get; set; }

    // Required when updating; the value returned by the previous save.
    public string? RowVersion { get; set; }

    [StringLength(200)] public string Title { get; set; } = "";
    [StringLength(50)] public string? RequestType { get; set; }
    [StringLength(50)] public string? WorkType { get; set; }
    [StringLength(20)] public string? Priority { get; set; }
    [StringLength(50)] public string? RequestSource { get; set; }
    public DateOnly? DateReceived { get; set; }

    [StringLength(20)] public string? CodeMode { get; set; }
    [StringLength(10)] public string? Code { get; set; }
    [StringLength(500)] public string? Descriptor { get; set; }
    [StringLength(500)] public string? Parenthetical { get; set; }
    [StringLength(200)] public string? Manufacturer { get; set; }
    [StringLength(200)] public string? Product { get; set; }
    [StringLength(20)] public string? SiteOfService { get; set; }

    public DateOnly? EffectiveDate { get; set; }
    [StringLength(50)] public string? Jurisdiction { get; set; }
    public DateOnly? DueDate { get; set; }
    [StringLength(100)] public string? Assignee { get; set; }
    public bool Urgent { get; set; }

    public RoutingDto? Routing { get; set; }
    [MaxLength(20)] public List<WorkPortionDto>? WorkPortions { get; set; }
    public bool NotifyTeams { get; set; }
    public bool LockRouting { get; set; }

    [StringLength(4000)] public string? IssueSummary { get; set; }
    [StringLength(500)] public string? ComparatorCodes { get; set; }
    [StringLength(2000)] public string? SpecialInstructions { get; set; }

    // Ids returned by POST /api/attachments. The full list replaces what is currently attached.
    [MaxLength(25)] public List<Guid>? AttachmentIds { get; set; }
}

public record SaveCaseResponse(Guid Id, string CaseNumber, string Status, string RowVersion, DateTimeOffset UpdatedAt);

public record AttachmentDto(Guid Id, string Name, long Size, string ContentType);

public record CaseDetail(
    Guid Id, string CaseNumber, string Status, string RowVersion,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? SubmittedAt,
    SaveCaseRequest Data, IReadOnlyList<AttachmentDto> Attachments);

public record CaseSummary(Guid Id, string CaseNumber, string Status, string Title, string? Code, string Priority,
    DateOnly? DueDate, DateTimeOffset UpdatedAt);
