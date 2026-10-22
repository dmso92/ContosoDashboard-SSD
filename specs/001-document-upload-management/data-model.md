# Data Model: Document Upload and Management

## Document

Represents one available, scanned work file and its searchable metadata.

| Field | Type / constraints | Notes |
|---|---|---|
| `DocumentId` | Primary key | Generated database identifier. |
| `Title` | Required, max 255 | User-facing title; not a storage name. |
| `Description` | Optional, max 2000 | Searchable text. |
| `Category` | Required enum | Project Documents, Team Resources, Personal Files, Reports, Presentations, Other. |
| `OriginalFileName` | Required, max 255 | Display metadata only; never used as a filesystem path. |
| `StorageKey` | Required, unique | Server-generated relative key; no absolute path or user-controlled path segment. |
| `ContentType` | Required, max 255 | Server-validated MIME type for the allowlisted extension/content family. |
| `FileSizeBytes` | Required, positive; at most the configured 25 MB byte limit | Each file is validated independently; the limit is inclusive and represented by one server-side byte constant. |
| `UploadedAtUtc` | Required | Set by the server. |
| `UploaderUserId` | Required FK to `User` | Owner/uploader. |
| `ProjectId` | Optional FK to `Project` | Direct project association, if selected. |
| `TaskId` | Optional FK to `TaskItem` | A task association implies the task's current project association. |

**Relationships and rules**

- One user uploads many documents; deleting a user must not cascade-delete stored file metadata implicitly.
- A document may be linked to a project, a task, both consistently, or neither. If `TaskId` is present, derive `ProjectId` from the task and reject a mismatched project supplied by the client.
- Only a fully scanned file has a persisted available `Document` record. Temporary quarantine state is outside the document catalog and is removed when scan or persistence fails.
- File type is enforced server-side using extension allowlists and content checks; accepted families are PDF, Word, Excel, PowerPoint, plain text, JPEG, and PNG.
- Search, filter, and sort operations must always apply the requester's access predicate before returning results.
- Recommended indexes: uploader/date, project/date, task, category/date, and unique storage key. Index choices must be checked against representative list/search measurements.

## DocumentTag

Normalized tag attached to a document.

| Field | Type / constraints | Notes |
|---|---|---|
| `DocumentTagId` | Primary key | Generated identifier. |
| `DocumentId` | Required FK to `Document` | Cascade removal with its document. |
| `Value` | Required, bounded length | Trimmed and normalized for search/deduplication; validate per-tag length and count in implementation. |

**Relationship**: One document has zero or more tags. Enforce uniqueness on normalized `(DocumentId, Value)`. Tags are included in document search but do not grant access.

## DocumentShare

An explicit access grant created by a document owner.

| Field | Type / constraints | Notes |
|---|---|---|
| `DocumentShareId` | Primary key | Generated identifier. |
| `DocumentId` | Required FK to `Document` | Removed with the document. |
| `RecipientUserId` | Nullable FK to `User` | Set for a direct-user share. |
| `RecipientDepartment` | Nullable, bounded string | Set for a department/team share and resolved against the recipient's current department. |
| `CreatedByUserId` | Required FK to `User` | Share owner/creator. |
| `CreatedAtUtc` | Required | Server timestamp. |

**Validation and access rules**

- Exactly one of `RecipientUserId` and `RecipientDepartment` is set.
- The share creator must own the document. Duplicate active grants for the same document/recipient are rejected.
- A direct-user grant remains valid until removed or the document is deleted. A department grant applies only while a user's current `Department` matches the grant.
- A share supplements, but never bypasses, authenticated access checks; it does not expose a static or permanent file URL.
- Because the existing application has no Team entity and models team membership by `User.Department`, department is the planned “team” recipient and team-lead scope.

## DocumentActivity

Append-only record of document actions used for administrator reporting and access review.

| Field | Type / constraints | Notes |
|---|---|---|
| `DocumentActivityId` | Primary key | Generated identifier. |
| `DocumentId` | Nullable FK to `Document` | Nullable for denied requests where no valid document is resolved. |
| `ActorUserId` | Nullable FK to `User` | Authenticated actor when known. |
| `Action` | Required enum | Upload, Download, Preview, Delete, Share, Unshare, DeniedAccess. |
| `OccurredAtUtc` | Required | Server timestamp. |
| `OutcomeCode` | Optional bounded code | Use non-sensitive result categories; do not store file content, raw exception text, credentials, or storage paths. |
| `ContentTypeAtEvent` | Optional, max 255 | Snapshot content type for reporting uploads after the document itself is deleted. |

**Rules**

- Record uploads, downloads, successful previews, deletions, sharing changes, and denied access attempts.
- A denied request must not return document metadata or content to the requester. The event is for authorized administrative reporting only.
- Avoid cascading deletion of audit records when a document is deleted; set the optional document reference to null and retain minimal event facts that support reporting without retaining the deleted file or access grant.
- Administrators may aggregate by content type, uploader, action, and time to satisfy the report requirements.

## Existing entities and authorization relationships

- `User` / `UserRole`: provide identity, owner, role, and current `Department`.
- `Project` / `ProjectMember`: project manager and current project-member relationship determine project access; manager scope is `Project.ProjectManagerId`.
- `TaskItem`: optional task association; its project is authoritative when a task is selected.
- `Notification`: extend notification types or otherwise distinguish document-share and project-document notices while continuing to use the existing per-user in-app notification service.

## Document lifecycle

1. **Selected → staged**: Validate required per-file metadata, size, and allowlisted type; write to a generated quarantine filename outside `wwwroot`.
2. **Staged → scanned**: Run the configured local scanner. Any unavailable, timed-out, incomplete, infected, or ambiguous result rejects the file and deletes the staged content.
3. **Scanned clean → available**: Move to a generated final storage key, persist metadata and tags, then expose only through authorized service queries/endpoints. If persistence fails, remove the final file as compensation.
4. **Available → replaced**: Reauthorize, stage and scan replacement content, then atomically make the new current file/metadata visible and clean up the old file. No prior-file history or rollback is retained.
5. **Available → deleted**: Require explicit UI confirmation and deletion permission; revoke shares and remove the file and catalog record. Do not create a recoverable trash state.

Filesystem and SQL operations are not one transaction; implementation must use explicit cleanup/compensation and must not expose partially completed content.
