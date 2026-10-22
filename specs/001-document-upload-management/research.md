# Research: Document Upload and Management

## Decisions

### Existing application stack and boundaries

- **Decision**: Keep the feature in the current ASP.NET Core 8 / Blazor Server application, using EF Core 8 with SQL Server LocalDB and the established Models, Data, Services, Pages, and dependency-injection patterns.
- **Rationale**: `ContosoDashboard.csproj`, `Program.cs`, `ApplicationDbContext`, and the current services show that these are already the supported application boundaries. The constitution requires preserving this stack and local/offline behavior.
- **Alternatives considered**: A separate API/backend or cloud service was rejected because it adds infrastructure and conflicts with the offline training constraint. A rewrite of the existing Razor/Blazor patterns is out of scope.

### Local file storage and HTTP delivery

- **Decision**: Store files in an application-managed directory outside `wwwroot`, using generated opaque storage keys; serve content only through authenticated endpoints that authorize each request.
- **Rationale**: This prevents static-file middleware from bypassing access checks and avoids using user-supplied filenames as paths. ASP.NET Core's upload guidance recommends controlled upload handling and notes that uploaded files should not be stored in the app tree where they can be served directly. OWASP also recommends storage outside the web root, allowlisted types, generated filenames, and malware scanning.
- **Alternatives considered**: Storing under `wwwroot` was rejected because URLs would bypass document-level checks. A cloud object store was rejected because it is explicitly out of scope and core operation must be offline. Returning permanent local paths to the browser was rejected because paths are implementation details and access must be rechecked.

### Malware scanning in offline training

- **Decision**: Define an `IFileScanner` boundary and provide a local ClamAV command-line adapter; do not make uploaded content available unless a complete scan returns an explicit clean result.
- **Rationale**: `clamscan` can scan locally without an always-running daemon and distinguishes clean, infected, and error outcomes. An adapter keeps the application flow independent of scanner-specific process details. Quarantine-before-scan and fail-closed handling cover unavailable, timed-out, incomplete, and unexpected scan outcomes.
- **Alternatives considered**:
  - **Microsoft Defender command-line**: native to Windows, but documented exit codes conflate a clean scan with a detection that was remediated, and the command requires elevation; this is less straightforward as an unambiguous per-upload gate.
  - **AMSI**: a Windows scanning interface rather than a scanner engine; it depends on a configured provider and requires more integration than the selected local CLI adapter.
  - **No application scanner / extension checks only**: rejected because the feature explicitly requires malware scanning and says failure to scan must fail closed.
- **Operational constraint**: ClamAV installation and signature provisioning/refresh must be managed for each training host. The scanner executable and signature database must be available locally. If the host cannot satisfy this prerequisite, upload remains disabled/rejected; no fallback accepts the file.

### Document permissions and team scope

- **Decision**: Put authorization in document service operations and repeat the check at each content endpoint; derive access from the current owner, project assignment/management, current team department, administrator role, or explicit share.
- **Rationale**: Existing services such as `ProjectService` accept the requesting user ID and check project manager/member relationships server-side. `UserService.GetTeamMembersAsync` treats equal `Department` values as team membership, and the `User` model has no separate Team entity. Using current department membership avoids introducing a parallel team model and allows team access to change with current user records.
- **Alternatives considered**: UI-only hiding was rejected by the constitution. A new Team/TeamMember schema was rejected because the existing code has no such concept and the spec's training assumptions can be supported by the current department model. Static share URLs were rejected because they cannot safely represent changing access.
- **Clarification resolved**: For this implementation plan, “team” means the user's current department. A future requirement for independent teams or multi-team membership would require a separately specified model change.

### Search, performance, and audit

- **Decision**: Keep list/search queries in EF-backed services, scope to authorized rows before filtering/sorting/results, add indexes based on owner/project/task/category/upload-time/share access patterns, and write activity records for required document events.
- **Rationale**: Existing services use LINQ/EF Core. The spec sets targets for 500 documents and search within 2 seconds; server-side filtering, projection, and scoped indexes are appropriate at this scale. The audit entity supports administrator reports without logging file contents or raw storage paths.
- **Alternatives considered**: An external search service was rejected because the application must work offline and the specified corpus is modest. Storing whole files or tag lists in a single metadata column was rejected in favor of filesystem storage and normalized searchable tag records.

### EF Core schema changes

- **Decision**: Add document, share, tag, and activity mappings and indexes through an explicit EF Core schema-evolution plan; handle the current `EnsureCreated()`-based LocalDB separately and preserve existing data.
- **Rationale**: `ApplicationDbContext` has no document schema or migration files, while `Program.cs` currently initializes with `Database.EnsureCreated()`. `EnsureCreated()` does not evolve an existing schema. The implementation must avoid assuming that adding entities alone updates already-created databases.
- **Alternatives considered**: Runtime drop/recreate was rejected because it risks existing training data. Continuing with `EnsureCreated()` as the only schema operation was rejected because it will not safely add feature tables to an existing database.

### Focused validation

- **Decision**: Add a small .NET 8 xUnit test project, use SQLite in-memory for relational service checks, and use fakes for storage/scanner boundaries; run the app build and focused manual browser checks.
- **Rationale**: The repository currently contains only the web project and no test project or test files. The feature has authorization, file lifecycle, and integration behavior that needs repeatable permitted/denied and failure-path checks. A fake scanner makes test outcomes deterministic without requiring a host scanner.
- **Alternatives considered**: Manual-only checks were rejected for security-sensitive rules. SQL Server LocalDB-only tests were rejected as the sole automated suite because they require a local database instance and reduce portability. An EF in-memory provider alone was not selected because relational constraints and query behavior matter.

## Sources

- Repository implementation: `ContosoDashboard/Program.cs`, `ContosoDashboard/ContosoDashboard.csproj`, `ContosoDashboard/Data/ApplicationDbContext.cs`, and existing `Services/ProjectService.cs`, `Services/UserService.cs`, and `Services/DashboardService.cs`.
- Feature requirements: `specs/001-document-upload-management/spec.md` and `StakeholderDocs/document-upload-and-management-feature.md`.
- Microsoft, ASP.NET Core file uploads (.NET 8): <https://learn.microsoft.com/aspnet/core/mvc/models/file-uploads?view=aspnetcore-8.0>
- OWASP, File Upload Cheat Sheet: <https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html>
- ClamAV, scanning guide: <https://docs.clamav.net/manual/Usage/Scanning.html>
- ClamAV, Windows installation from source: <https://docs.clamav.net/manual/Installing/Installing-from-source-Windows.html>
- ClamAV, signature management: <https://docs.clamav.net/manual/Usage/SignatureManagement.html>
- Microsoft Defender Antivirus command-line arguments: <https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus>
- Microsoft, Antimalware Scan Interface overview: <https://learn.microsoft.com/en-us/windows/win32/amsi/antimalware-scan-interface-portal>
