namespace PricingApi.Domain;

public enum CaseStatus
{
    Draft = 0,
    Open = 1,
}

public class PricingCase
{
    public Guid Id { get; set; }
    public string CaseNumber { get; set; } = "";
    public CaseStatus Status { get; set; }

    // Entra ID object id ("oid") of the creator; drafts are private to this user.
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";

    public string Title { get; set; } = "";
    public string? RequestType { get; set; }
    public string? WorkType { get; set; }
    public string Priority { get; set; } = "Medium";
    public string? RequestSource { get; set; }
    public DateOnly? DateReceived { get; set; }

    public string CodeMode { get; set; } = "assigned";
    public string? Code { get; set; }
    public string? Descriptor { get; set; }
    public string? Parenthetical { get; set; }
    public string? Manufacturer { get; set; }
    public string? Product { get; set; }
    public string? SiteOfService { get; set; }

    public DateOnly? EffectiveDate { get; set; }
    public string? Jurisdiction { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? Assignee { get; set; }
    public bool Urgent { get; set; }

    public RoutingPlan Routing { get; set; } = new();
    public List<WorkPortion> WorkPortions { get; set; } = [];
    public bool NotifyTeams { get; set; }
    public bool LockRouting { get; set; }

    public string? IssueSummary { get; set; }
    public string? ComparatorCodes { get; set; }
    public string? SpecialInstructions { get; set; }

    public List<CaseAttachment> Attachments { get; set; } = [];

    public int Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }

    // Optimistic concurrency token: a stale browser tab cannot overwrite a newer save.
    public byte[] RowVersion { get; set; } = [];
}

public class PricingCaseVersion
{
    public Guid Id { get; set; }
    public Guid CaseId { get; set; }
    public int Revision { get; set; }
    public CaseStatus Status { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    public string SavedBy { get; set; } = "";
    public string DataJson { get; set; } = "";
    public string AttachmentsJson { get; set; } = "";
}

public class RoutingPlan
{
    public bool ClinicalJudgment { get; set; }
    public bool NovelTechnology { get; set; }
    public bool MethodologyChanges { get; set; }
    public string? Rationale { get; set; }
    public bool TechnologyAssessmentRequired { get; set; }
    public bool CmdReviewRequired { get; set; }
}

public class WorkPortion
{
    public int Sequence { get; set; }
    public string Name { get; set; } = "";
    public bool Applicable { get; set; }
    public string? Team { get; set; }
    public DateOnly? DueDate { get; set; }
}

public class CaseAttachment
{
    public Guid Id { get; set; }

    // Null until the file is linked to a saved case (uploads happen before the first save).
    public Guid? CaseId { get; set; }
    public string DraftKey { get; set; } = "";

    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public string StorageKey { get; set; } = "";

    public string UploadedBy { get; set; } = "";
    public DateTimeOffset UploadedAt { get; set; }
}
