# Quickstart: Document Upload and Management Validation

This guide validates the implemented feature in the repository's local training environment only; mock login and local scanning are not production security assurances.

## Prerequisites

- Windows with the .NET 8 SDK.
- SQL Server LocalDB available using the existing `DefaultConnection`.
- ClamAV `clamscan` installed locally with a usable local signature database. Configure `Documents:Scanner:ExecutablePath` (or `Documents__Scanner__ExecutablePath`) if it is not on `PATH`; uploads fail closed if the scanner is missing, times out, or returns an ambiguous result.
- A writable document storage directory outside `ContosoDashboard/wwwroot`. The default is `%LOCALAPPDATA%\ContosoDashboard\Documents`; an override can be supplied with `Documents__Storage__RootPath`, but it must remain outside the web root.
- A populated training database with test users representing Employee, TeamLead, ProjectManager, and Administrator roles, plus project members and users in at least two departments.
- A clean supported sample file for each format under test, an unsupported-format sample, a file at the 25 MiB boundary, and a file above the limit.
- The `ContosoDashboard.Tests` test project.

## Build and automated checks

From the repository root:

```powershell
dotnet restore .\ContosoDashboard\ContosoDashboard.csproj
dotnet build .\ContosoDashboard\ContosoDashboard.csproj
dotnet test .\ContosoDashboard.Tests\ContosoDashboard.Tests.csproj
```

Expected: restore/build succeed; focused tests cover authorization allow/deny, scanner clean/infected/unavailable outcomes, per-file validation, task/project consistency, and authenticated file routes.

**Execution record (2026-10-22)**: These commands could not be run in the implementation environment because the `dotnet` executable is not installed or available on `PATH`. Install the .NET 8 SDK and rerun the complete commands above before accepting or deploying this training feature. SQL Server LocalDB and ClamAV provisioning were also unavailable for end-to-end checks here.

## Implementation boundary review

The source review confirms that uploads use server-generated keys, remain outside `wwwroot`, and are published only after a clean scanner verdict; scanner errors fail closed. Search and file delivery are scoped to current persisted ownership, project/task participation, department membership, explicit shares, or administrator access. Preview/download routes require authentication, reauthorize on each request, use validated MIME types and `nosniff`, and do not return storage paths. Upload persistence compensates by deleting the published file if metadata persistence fails; deletion removes the catalog entry and then its local file. The schema initializer uses additive, idempotent SQL for existing LocalDB databases and does not drop existing tables. Runtime database, scanner, and browser checks remain outstanding in this environment.

## Run the application

```powershell
dotnet run --project .\ContosoDashboard\ContosoDashboard.csproj
```

Sign in through the existing training login page with a role-appropriate user. Verify document navigation appears within the existing dashboard layout.

## End-to-end scenarios

1. **Upload and quarantine**
   - Upload an allowed clean PDF with title/category and optional tags.
   - Expected: progress and a per-file success result; metadata appears in My Documents; the file is stored outside `wwwroot`; the scanner reports clean before it is downloadable.
   - Upload unsupported, over-limit, infected, and scan-unavailable files.
   - Expected: a specific perceivable failure for each; no document is exposed and staging content is cleaned up. A file exactly at the configured inclusive 25 MB byte limit is accepted when its type and scan are valid.
2. **Batch independence**
   - Submit multiple files containing both valid and invalid members.
   - Expected: each member has its own progress/result; success for one is not hidden by another's failure.
3. **Search, filter, and performance**
   - Prepare authorized documents with varied titles, tags, categories, dates, projects, and uploaders. Search and filter/sort by every supported field; include up to 500 visible records.
   - Expected: results contain only authorized documents and each supported list/search operation meets the 2-second target under representative local conditions.
4. **Project, task, and share access**
   - Open project and task document panels as an assigned member, a non-member, and a project manager; upload from a permitted task; share directly and to a department.
   - Expected: task association resolves to the task's project; current project/department membership is checked; recipients see valid shares and notifications; stale or revoked membership does not authorize old links.
5. **Edit, replace, and delete**
   - Edit metadata and replace a file as owner, eligible team lead, project manager, and administrator. Attempt disallowed edits/deletion as a different team/project user; delete an authorized document after confirmation.
   - Expected: each role gets only FR-016/FR-017 permissions; replacement has no prior version/rollback; delete removes the file and shared availability; denied attempts make no change.
6. **Preview/download and audit**
   - Preview a permitted PDF/image, download an allowed non-preview type, then attempt direct endpoint access with an unauthorized identity and a stale link.
   - Expected: permitted content is delivered with safe headers; unsupported previews offer a controlled explanation; denied requests return no document metadata/content. Upload, download, successful preview, deletion, share, and denied access events appear in administrator reports.
7. **Dashboard and accessibility**
   - Check dashboard recent uploads and count as users with different access scopes; operate upload, validation, share, and confirmation dialogs with keyboard and assistive technology.
   - Expected: recent list contains the current user's five newest uploads, counts include only visible documents, and labels/progress/errors/status remain perceivable without color alone.

For entity and relationship definitions see [data-model.md](data-model.md); for browser and transfer behavior see [contracts/document-management.md](contracts/document-management.md).
