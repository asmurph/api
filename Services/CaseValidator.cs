using System.Text.RegularExpressions;
using PricingApi.Dtos;

namespace PricingApi.Services;

/// <summary>
/// Draft saves are lenient (title only) so users can save partial work.
/// Submit enforces every field the form marks as required.
/// </summary>
public static partial class CaseValidator
{
    public static readonly string[] RequestTypes =
    [
        "CR", "TDLR", "Internal Inquiry", "External inquiry", "Recondsideration Request",
        "Senior Leadershipt Request", "CMS Request", "DOJ", "OIG", "UPIC", "FOIA",
    ];
    public static readonly string[] WorkTypes =
    [
        "Ambulatory Surgical Center", "Blood products", "Category 3 Codes", "Clinical Lab", "Critical Access Hospital",
        "Drugs/biologicals", "Indicator 1", "MPFS/RU", "Portable X-ray", "Radio Pharmaceuticals", "TPNIES",
    ];
    public static readonly string[] Priorities = ["High", "Medium", "Low"];
    public static readonly string[] RequestSources = ["CMS", "Contractor", "Internal"];
    public static readonly string[] Jurisdictions = ["JH", "JL", "JN", "ALL"];
    public static readonly string[] Sites = ["Facility", "Nonfacility", "Both"];
    public static readonly string[] Teams = ["Pricing", "Clinical", "QA", "Systems"];
    public static readonly string[] PortionNames =
    [
        "Intake validation", "Code evaluation", "Technology assessment", "Pricing methodology and calculation",
        "Methodology approval", "CMD review", "Independent quality review", "Systems implementation",
    ];

    [GeneratedRegex("^[A-Za-z0-9]{4,10}$")]
    private static partial Regex CodePattern();

    public static Dictionary<string, string[]> Validate(SaveCaseRequest r, bool submit)
    {
        var e = new Dictionary<string, string[]>();
        void Add(string key, string msg) => e[key] = [msg];

        if (string.IsNullOrWhiteSpace(r.Title)) Add("title", "A case title is required.");

        void OneOf(string key, string? value, string[] allowed, bool required)
        {
            if (string.IsNullOrWhiteSpace(value)) { if (required) Add(key, "Required."); return; }
            if (!allowed.Contains(value)) Add(key, "Not a valid value.");
        }

        OneOf("requestType", r.RequestType, RequestTypes, submit);
        OneOf("workType", r.WorkType, WorkTypes, submit);
        OneOf("requestSource", r.RequestSource, RequestSources, submit);
        OneOf("jurisdiction", r.Jurisdiction, Jurisdictions, submit);
        OneOf("priority", r.Priority, Priorities, false);
        OneOf("siteOfService", r.SiteOfService, Sites, false);

        if (r.CodeMode is not (null or "assigned" or "pending")) Add("codeMode", "Must be 'assigned' or 'pending'.");
        if (r.CodeMode == "assigned" || r.CodeMode is null)
        {
            if (!string.IsNullOrWhiteSpace(r.Code) && !CodePattern().IsMatch(r.Code.Trim()))
                Add("code", "Use 4-10 letters or digits.");
            else if (submit && string.IsNullOrWhiteSpace(r.Code))
                Add("code", "Required when a code is assigned.");
        }

        if (submit && r.EffectiveDate is null) Add("effectiveDate", "Required.");
        if (r.DueDate is { } due && r.DateReceived is { } rec && due < rec)
            Add("dueDate", "Due date cannot be before the date received.");

        if (r.WorkPortions is { } ps)
        {
            if (ps.Any(p => !PortionNames.Contains(p.Name))) Add("workPortions", "Unknown work portion.");
            else if (ps.Select(p => p.Name).Distinct().Count() != ps.Count) Add("workPortions", "Duplicate work portion.");
            else if (ps.Any(p => !string.IsNullOrEmpty(p.Team) && !Teams.Contains(p.Team)))
                Add("workPortions", "Unknown team.");
        }

        return e;
    }
}
