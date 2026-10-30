SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @Now DATETIMEOFFSET = SYSUTCDATETIME();

    -- ============================================================================
    -- Case 1: Draft case with multiple attachments (not yet submitted)
    -- ============================================================================
    DECLARE @Case1Id UNIQUEIDENTIFIER = NEWID();

    INSERT INTO dbo.Cases
    (
        Id, CaseNumber, Status, OwnerId, OwnerName, Title, RequestType, WorkType, Priority, RequestSource,
        DateReceived, CodeMode, Code, Descriptor, Parenthetical, Manufacturer, Product, SiteOfService,
        EffectiveDate, Jurisdiction, DueDate, Assignee, Urgent,
        Routing_ClinicalJudgment, Routing_NovelTechnology, Routing_MethodologyChanges, Routing_Rationale,
        Routing_TechnologyAssessmentRequired, Routing_CmdReviewRequired,
        NotifyTeams, LockRouting, IssueSummary, ComparatorCodes, SpecialInstructions,
        Revision, CreatedAt, UpdatedAt, SubmittedAt
    )
    VALUES
    (
        @Case1Id, N'PC-2026-0101', N'Draft', N'user-oid-001', N'Case Owner One', N'Draft case for pricing review',
        N'New', N'Clinical', N'Medium', N'Portal',
        CONVERT(date, @Now), N'assigned', N'A1234', N'Initial draft descriptor', N'Initial parenthetical', N'ACME', N'Widget A', N'Facility',
        CONVERT(date, DATEADD(DAY, 7, @Now)), N'National', CONVERT(date, DATEADD(DAY, 14, @Now)), N'Analyst A', 0,
        1, 0, 1, N'Draft route rationale',
        0, 1,
        1, 0, N'Initial issue summary', N'B2222,C3333', N'Please review draft',
        1, @Now, @Now, NULL
    );

    INSERT INTO dbo.CaseWorkPortions (Sequence, Name, Applicable, Team, DueDate, CaseId)
    VALUES
        (1, N'Clinical review', 1, N'Clinical Ops', CONVERT(date, DATEADD(DAY, 5, @Now)), @Case1Id),
        (2, N'Technology assessment', 1, N'Tech Team', CONVERT(date, DATEADD(DAY, 8, @Now)), @Case1Id),
        (3, N'CMD review', 1, N'CMD', CONVERT(date, DATEADD(DAY, 10, @Now)), @Case1Id);

    DECLARE @Case1Att1 UNIQUEIDENTIFIER = NEWID();
    DECLARE @Case1Att2 UNIQUEIDENTIFIER = NEWID();

    INSERT INTO dbo.Attachments
    (
        Id, CaseId, DraftKey, FileName, ContentType, Size, StorageKey, UploadedBy, UploadedAt
    )
    VALUES
        (@Case1Att1, @Case1Id, N'draft-key-001', N'clinical-notes.pdf', N'application/pdf', 153245, N'blob-key-case1-a1', N'user-oid-001', @Now),
        (@Case1Att2, @Case1Id, N'draft-key-001', N'comparator-data.xlsx', N'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 90422, N'blob-key-case1-a2', N'user-oid-001', @Now);

    -- ============================================================================
    -- Case 2: Submitted/Open case with update history and file publish versions
    -- ============================================================================
    DECLARE @Case2Id UNIQUEIDENTIFIER = NEWID();

    INSERT INTO dbo.Cases
    (
        Id, CaseNumber, Status, OwnerId, OwnerName, Title, RequestType, WorkType, Priority, RequestSource,
        DateReceived, CodeMode, Code, Descriptor, Parenthetical, Manufacturer, Product, SiteOfService,
        EffectiveDate, Jurisdiction, DueDate, Assignee, Urgent,
        Routing_ClinicalJudgment, Routing_NovelTechnology, Routing_MethodologyChanges, Routing_Rationale,
        Routing_TechnologyAssessmentRequired, Routing_CmdReviewRequired,
        NotifyTeams, LockRouting, IssueSummary, ComparatorCodes, SpecialInstructions,
        Revision, CreatedAt, UpdatedAt, SubmittedAt
    )
    VALUES
    (
        @Case2Id, N'PC-2026-0102', N'Open', N'user-oid-002', N'Case Owner Two', N'Open case with version history',
        N'Update', N'Methodology', N'High', N'Email',
        CONVERT(date, DATEADD(DAY, -20, @Now)), N'assigned', N'J1100', N'Current descriptor v3', N'Current parenthetical v3', N'ACME', N'Widget B', N'Hospital',
        CONVERT(date, DATEADD(DAY, -10, @Now)), N'State', CONVERT(date, DATEADD(DAY, 3, @Now)), N'Analyst B', 1,
        1, 1, 0, N'Current routing rationale',
        1, 1,
        1, 0, N'Current issue summary', N'X1000,Y2000', N'Escalated case',
        3, DATEADD(DAY, -20, @Now), DATEADD(DAY, -1, @Now), DATEADD(DAY, -18, @Now)
    );

    INSERT INTO dbo.CaseWorkPortions (Sequence, Name, Applicable, Team, DueDate, CaseId)
    VALUES
        (1, N'Clinical review', 1, N'Clinical Ops', CONVERT(date, DATEADD(DAY, -15, @Now)), @Case2Id),
        (2, N'Technology assessment', 1, N'Tech Team', CONVERT(date, DATEADD(DAY, -12, @Now)), @Case2Id),
        (3, N'CMD review', 1, N'CMD', CONVERT(date, DATEADD(DAY, -8, @Now)), @Case2Id);

    DECLARE @Case2Att1 UNIQUEIDENTIFIER = NEWID();
    DECLARE @Case2Att2 UNIQUEIDENTIFIER = NEWID();
    DECLARE @Case2Att3 UNIQUEIDENTIFIER = NEWID();

    INSERT INTO dbo.Attachments
    (
        Id, CaseId, DraftKey, FileName, ContentType, Size, StorageKey, UploadedBy, UploadedAt
    )
    VALUES
        (@Case2Att1, @Case2Id, N'', N'source-request.docx', N'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 67212, N'blob-key-case2-a1', N'user-oid-002', DATEADD(DAY, -19, @Now)),
        (@Case2Att2, @Case2Id, N'', N'clinical-summary.pdf', N'application/pdf', 210443, N'blob-key-case2-a2', N'user-oid-002', DATEADD(DAY, -17, @Now)),
        (@Case2Att3, @Case2Id, N'', N'cost-model.xlsx', N'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 341245, N'blob-key-case2-a3', N'user-oid-002', DATEADD(DAY, -2, @Now));

    -- Snapshot history: revisions 1 and 2 saved as JSON snapshots
    INSERT INTO dbo.CaseVersions
    (
        Id, CaseId, Revision, Status, SavedAt, SavedBy, DataJson, AttachmentsJson
    )
    VALUES
    (
        NEWID(), @Case2Id, 1, N'Draft', DATEADD(DAY, -18, @Now), N'user-oid-002',
        N'{"title":"Open case with version history","priority":"High","code":"J1100","submit":true}',
        N'[{"id":"' + CONVERT(nvarchar(36), @Case2Att1) + N'","name":"source-request.docx","size":67212,"contentType":"application/vnd.openxmlformats-officedocument.wordprocessingml.document"}]'
    ),
    (
        NEWID(), @Case2Id, 2, N'Open', DATEADD(DAY, -5, @Now), N'user-oid-002',
        N'{"title":"Open case with version history","priority":"High","code":"J1100","submit":false}',
        N'[{"id":"' + CONVERT(nvarchar(36), @Case2Att1) + N'","name":"source-request.docx","size":67212,"contentType":"application/vnd.openxmlformats-officedocument.wordprocessingml.document"},{"id":"' + CONVERT(nvarchar(36), @Case2Att2) + N'","name":"clinical-summary.pdf","size":210443,"contentType":"application/pdf"}]'
    );

    -- SharePoint publish metadata per case revision
    INSERT INTO dbo.CaseFileVersions
    (
        Id, CaseId, AttachmentId, CaseRevision, FileName, SharePointItemId, SharePointWebUrl,
        SharePointETag, SharePointVersion, UploadedAt, UploadedBy
    )
    VALUES
    (
        NEWID(), @Case2Id, @Case2Att1, 2, N'source-request.docx', N'101',
        N'https://contoso.sharepoint.com/sites/pricing/Shared%20Documents/Request-1002/Version-2/source-request.docx',
        N'"{A1B2C3},1"', N'2.0', DATEADD(DAY, -5, @Now), N'user-oid-002'
    ),
    (
        NEWID(), @Case2Id, @Case2Att2, 2, N'clinical-summary.pdf', N'102',
        N'https://contoso.sharepoint.com/sites/pricing/Shared%20Documents/Request-1002/Version-2/clinical-summary.pdf',
        N'"{D4E5F6},1"', N'2.0', DATEADD(DAY, -5, @Now), N'user-oid-002'
    ),
    (
        NEWID(), @Case2Id, @Case2Att1, 3, N'source-request.docx', N'201',
        N'https://contoso.sharepoint.com/sites/pricing/Shared%20Documents/Request-1002/Version-3/source-request.docx',
        N'"{A1B2C3},2"', N'3.0', DATEADD(DAY, -1, @Now), N'user-oid-002'
    ),
    (
        NEWID(), @Case2Id, @Case2Att2, 3, N'clinical-summary.pdf', N'202',
        N'https://contoso.sharepoint.com/sites/pricing/Shared%20Documents/Request-1002/Version-3/clinical-summary.pdf',
        N'"{D4E5F6},2"', N'3.0', DATEADD(DAY, -1, @Now), N'user-oid-002'
    ),
    (
        NEWID(), @Case2Id, @Case2Att3, 3, N'cost-model.xlsx', N'203',
        N'https://contoso.sharepoint.com/sites/pricing/Shared%20Documents/Request-1002/Version-3/cost-model.xlsx',
        N'"{G7H8I9},1"', N'1.0', DATEADD(DAY, -1, @Now), N'user-oid-002'
    );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO