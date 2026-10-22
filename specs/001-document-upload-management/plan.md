# Implementation Plan: Document Upload and Management

**Branch**: `not created (checkout is master)` | **Date**: 2026-10-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-document-upload-management/spec.md`

## Summary

Add secure, offline document management to the existing ASP.NET Core 8 / Blazor Server dashboard. Persist searchable metadata and access grants through EF Core and SQL Server LocalDB, store generated-name files outside `wwwroot`, and place an offline malware-scanning boundary between staging and availability. Keep authorization in document services and recheck it at every file-serving boundary. Integrate the document service with existing project, task, notification, and dashboard services; do not add a cloud dependency or production-security claim.

## Technical Context

- **Language/Version**: C# / .NET 8
- **Primary Dependencies**: ASP.NET Core 8 Blazor Server, EF Core 8, SQL Server LocalDB, existing cookie authentication and Bootstrap UI; local ClamAV scanner adapter behind an application interface
- **Storage**: SQL Server LocalDB for metadata and activity; host-local filesystem outside `wwwroot` for document content and temporary quarantine
- **Testing**: `dotnet build`; add a .NET 8 xUnit test project using SQLite in-memory for relational service tests and fakes for scanner/storage boundaries; browser/manual acceptance checks for Blazor behavior
- **Target Platform**: Existing local Windows training environment; core document operations must work without internet or cloud services
- **Project Type**: Single web application with server-rendered/interactive Blazor UI and authenticated file-transfer endpoints
- **Performance Goals**: Per FR-024: list/search of up to 500 documents within 2 seconds, PDF/image preview within 3 seconds, and upload of each file up to 25 MB within 30 seconds on a typical network
- **Constraints**: Supported formats and categories are allowlisted; 25 MB maximum per file (inclusive); scan must succeed before a file is made available; all scan failures fail closed; content stays outside the web root; enforce current owner/project/team/share permissions on queries and every operation; no version history, recovery area, external document provider, or cloud runtime
- **Scale/Scope**: One local training application; employee-level document collections, shared project/task associations, direct-user or department/team shares, and administrator activity reports. List performance is designed around the specified 500-document scenario.

## Constitution Check

*Gate evaluated before research and re-evaluated after design.*

| Principle / constraint | Gate | Plan evidence |
|---|---|---|
| I. Training-First Scope | PASS | Retain the training-only framing and explicitly state mock authentication and local scanner/storage setup are not production assurances. |
| II. Security and Privacy by Default | PASS | Authorization remains in server-side services and file endpoints; query scopes filter before results are returned; generated storage keys, out-of-webroot storage, fail-closed malware scanning, current membership checks, and privacy-minimizing audit events are planned. |
| III. Maintainable, Layered Design | PASS | Extend the existing Models/Data/Services/Pages arrangement and DI; keep storage, scanning, authorization/business rules, and UI responsibilities separated; no cloud implementation or unrelated refactor. |
| IV. Behavior Verified by Focused Checks | PASS | Plan includes automated service/authorization checks, build validation, and focused UI scenarios. The repository has no test project today, so a small .NET 8 test project is planned; current baseline validation is limited to the existing build. |
| V. Accessible, Consistent User Experience | PASS | Reuse the existing Blazor/Bootstrap navigation and layout; specify semantic controls, keyboard operation, and perceivable progress, validation, error, and status output. |
| Project Constraints: ASP.NET Core 8, Blazor Server, EF Core, LocalDB, local/offline operation | PASS | Preserve the current stack and use local file storage plus a configured offline scanner. |

**Gate result**: PASS. No constitution violations or scope exceptions are required. Operational prerequisite: the training host must provision the selected scanner and its signatures; until scanning is available, uploads remain unavailable rather than bypassing the scan.

**Post-design recheck**: PASS. The designed entities, service authorization boundary, authenticated file endpoints, local-only storage/scanning, focused automated checks, and accessible browser interactions remain aligned with all five principles and the project constraints. The scan dependency is explicitly fail-closed and documented as a training-host prerequisite, not a waiver.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-upload-management/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── contracts/
    └── document-management.md
```

### Source Code (repository root)

```text
ContosoDashboard/
├── Data/
│   └── ApplicationDbContext.cs             # document relationships, indexes, constraints
├── Models/
│   ├── Document.cs
│   ├── DocumentShare.cs
│   └── DocumentActivity.cs
├── Services/
│   ├── DocumentService.cs                  # query, authorization, lifecycle, reporting
│   ├── IFileStorageService.cs              # local file boundary
│   ├── LocalFileStorageService.cs
│   ├── IFileScanner.cs                     # scan boundary
│   └── ClamAvFileScanner.cs                # local offline adapter
├── Endpoints/
│   └── DocumentFileEndpoints.cs            # authenticated upload/preview/download
├── Pages/
│   ├── Documents.razor                     # browse, search, filter, edit, share
│   └── [existing dashboard/project/task pages extended for document panels]
└── wwwroot/
    └── [only browser-side upload progress support if needed; never stored document content]

ContosoDashboard.Tests/
├── Services/
├── Endpoints/
└── TestSupport/
```

**Structure Decision**: Extend the single existing `ContosoDashboard` web project. Keep EF entities in `Models`, persistence mappings in `Data/ApplicationDbContext.cs`, rules and orchestration in `Services`, UI in existing Razor components, and file-transfer routes in a dedicated endpoint module. Add a focused test project because none exists. Do not create a separate API/backend or a cloud-storage project.

## Design Decisions

1. **Active feature directory**: Write artifacts under the selected `.specify/feature.json` directory, `specs/001-document-upload-management`, not `specs/master`. The checked-out Git branch remains `master`; no feature branch is created by planning.
2. **Local storage and scan gate**: Stage under a non-public quarantine directory, scan before publishing or persisting an available document, then move to an application-generated final key. Delete staged content on rejection or scanner failure. Clean up the staged/final file if metadata persistence fails; file and database changes cannot share a single transaction.
3. **Scanner**: Use a narrow `IFileScanner` abstraction with a local ClamAV command-line adapter. Only an explicit clean verdict permits publication. Missing executable/database, timeout, incomplete scan, or any unexpected result rejects the upload. Host provisioning and signature-refresh procedures must be documented and verified.
4. **Authorization model**: Derive the requester from authenticated server identity, resolve the user and current roles/relationships, and scope each query/action in `DocumentService`; never accept an uploader ID or authorized storage path from the browser. Recheck authorization for preview/download endpoints. Department is the current codebase's team-membership proxy (`User.Department` and `UserService.GetTeamMembersAsync`); team-lead scope is a matching current department and the TeamLead role. Project scope uses current project manager/member records. Direct-user shares remain explicit grants until removed; department shares resolve against current department membership. Owner, managed-project, and administrator deletion permissions follow FR-017; team leads do not gain deletion rights over another user's document.
5. **File transfer**: Use authenticated HTTP endpoints for upload and content delivery rather than exposing local paths as static files. Submit one file per request so the Blazor UI can report independent outcomes and progress for each member of a selected batch. Deliver previews as inline only for allowed PDF/image MIME types and downloads as attachments.
6. **Metadata and links**: Store tags as related records for normalized filtering. Store optional `ProjectId` and `TaskId`; when a task is selected, derive and validate its project association from the task record. For project documents, check current project access rather than trusting a stale route or share link.
7. **Database evolution**: Add EF Core schema changes and indexes for document owner/project/task/date/category, shares, and activity queries. The existing app calls `EnsureCreated()` and has no migrations; implementation must establish a safe migration/baseline path for existing LocalDB installations and must not silently drop user data.
8. **Tests**: Introduce focused .NET 8 xUnit tests with SQLite in-memory for relational/service behavior and fakes for storage/scanner outcomes. Include both permitted and denied access, scanner failure, upload compensation, and file endpoint behavior.

## Complexity Tracking

No constitution gate violations. A test project and scanner/storage interfaces are proportionate to the feature's authorization and file-lifecycle boundaries and do not constitute an architectural exception.
