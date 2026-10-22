using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Data;

/// <summary>
/// Adds the document tables to LocalDB instances originally initialized with EnsureCreated.
/// Idempotent and additive: it never drops or rewrites existing training data.
/// </summary>
public static class DocumentSchemaInitializer
{
    public static async Task EnsureDocumentSchemaAsync(ApplicationDbContext context, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(context.Database.ProviderName, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal))
            return;

        const string sql = """
IF OBJECT_ID(N'dbo.Documents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Documents (
        DocumentId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Documents PRIMARY KEY,
        Title nvarchar(255) NOT NULL,
        Description nvarchar(2000) NULL,
        Category int NOT NULL,
        OriginalFileName nvarchar(255) NOT NULL,
        StorageKey nvarchar(64) NOT NULL,
        ContentType nvarchar(255) NOT NULL,
        FileSizeBytes bigint NOT NULL,
        UploadedAtUtc datetime2 NOT NULL,
        UploaderUserId int NOT NULL,
        ProjectId int NULL,
        TaskId int NULL,
        CONSTRAINT FK_Documents_Users_UploaderUserId FOREIGN KEY (UploaderUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_Documents_Projects_ProjectId FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId),
        CONSTRAINT FK_Documents_Tasks_TaskId FOREIGN KEY (TaskId) REFERENCES dbo.Tasks(TaskId)
    );
END;
IF OBJECT_ID(N'dbo.DocumentTags', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentTags (
        DocumentTagId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentTags PRIMARY KEY,
        DocumentId int NOT NULL,
        Value nvarchar(64) NOT NULL,
        CONSTRAINT FK_DocumentTags_Documents_DocumentId FOREIGN KEY (DocumentId) REFERENCES dbo.Documents(DocumentId) ON DELETE CASCADE
    );
END;
IF OBJECT_ID(N'dbo.DocumentShares', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentShares (
        DocumentShareId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentShares PRIMARY KEY,
        DocumentId int NOT NULL,
        RecipientUserId int NULL,
        RecipientDepartment nvarchar(100) NULL,
        CreatedByUserId int NOT NULL,
        CreatedAtUtc datetime2 NOT NULL,
        CONSTRAINT CK_DocumentShare_ExactlyOneRecipient CHECK
            ((CASE WHEN RecipientUserId IS NULL THEN 0 ELSE 1 END + CASE WHEN RecipientDepartment IS NULL THEN 0 ELSE 1 END) = 1),
        CONSTRAINT FK_DocumentShares_Documents_DocumentId FOREIGN KEY (DocumentId) REFERENCES dbo.Documents(DocumentId) ON DELETE CASCADE,
        CONSTRAINT FK_DocumentShares_Users_RecipientUserId FOREIGN KEY (RecipientUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_DocumentShares_Users_CreatedByUserId FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users(UserId)
    );
END;
IF OBJECT_ID(N'dbo.DocumentActivities', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentActivities (
        DocumentActivityId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentActivities PRIMARY KEY,
        DocumentId int NULL,
        ActorUserId int NULL,
        Action int NOT NULL,
        OccurredAtUtc datetime2 NOT NULL,
        OutcomeCode nvarchar(64) NULL,
        ContentTypeAtEvent nvarchar(255) NULL,
        CONSTRAINT FK_DocumentActivities_Documents_DocumentId FOREIGN KEY (DocumentId) REFERENCES dbo.Documents(DocumentId) ON DELETE SET NULL,
        CONSTRAINT FK_DocumentActivities_Users_ActorUserId FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(UserId) ON DELETE SET NULL
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_StorageKey' AND object_id = OBJECT_ID(N'dbo.Documents'))
    CREATE UNIQUE INDEX IX_Documents_StorageKey ON dbo.Documents(StorageKey);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_UploaderUserId_UploadedAtUtc' AND object_id = OBJECT_ID(N'dbo.Documents'))
    CREATE INDEX IX_Documents_UploaderUserId_UploadedAtUtc ON dbo.Documents(UploaderUserId, UploadedAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_ProjectId_UploadedAtUtc' AND object_id = OBJECT_ID(N'dbo.Documents'))
    CREATE INDEX IX_Documents_ProjectId_UploadedAtUtc ON dbo.Documents(ProjectId, UploadedAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_TaskId' AND object_id = OBJECT_ID(N'dbo.Documents'))
    CREATE INDEX IX_Documents_TaskId ON dbo.Documents(TaskId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_Category_UploadedAtUtc' AND object_id = OBJECT_ID(N'dbo.Documents'))
    CREATE INDEX IX_Documents_Category_UploadedAtUtc ON dbo.Documents(Category, UploadedAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentTags_DocumentId_Value' AND object_id = OBJECT_ID(N'dbo.DocumentTags'))
    CREATE UNIQUE INDEX IX_DocumentTags_DocumentId_Value ON dbo.DocumentTags(DocumentId, Value);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentShares_DocumentId_RecipientUserId' AND object_id = OBJECT_ID(N'dbo.DocumentShares'))
    CREATE UNIQUE INDEX IX_DocumentShares_DocumentId_RecipientUserId ON dbo.DocumentShares(DocumentId, RecipientUserId) WHERE RecipientUserId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentShares_DocumentId_RecipientDepartment' AND object_id = OBJECT_ID(N'dbo.DocumentShares'))
    CREATE UNIQUE INDEX IX_DocumentShares_DocumentId_RecipientDepartment ON dbo.DocumentShares(DocumentId, RecipientDepartment) WHERE RecipientDepartment IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentActivities_OccurredAtUtc_Action' AND object_id = OBJECT_ID(N'dbo.DocumentActivities'))
    CREATE INDEX IX_DocumentActivities_OccurredAtUtc_Action ON dbo.DocumentActivities(OccurredAtUtc, Action);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentActivities_ActorUserId' AND object_id = OBJECT_ID(N'dbo.DocumentActivities'))
    CREATE INDEX IX_DocumentActivities_ActorUserId ON dbo.DocumentActivities(ActorUserId);
""";
        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}
