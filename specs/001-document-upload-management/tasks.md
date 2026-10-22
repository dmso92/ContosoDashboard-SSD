# Tasks: Document Upload and Management

**Input**: Design documents from `/specs/001-document-upload-management/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: The feature specification defines independent acceptance tests for each user story, so the tasks below include focused service and endpoint checks before implementation.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the document feature test harness and the folder/file boundaries needed before authorization and storage work begins.

- [ ] T001 [P] Create the .NET 8 xUnit test project scaffold and shared fixtures under `ContosoDashboard.Tests/` and `ContosoDashboard.Tests/TestSupport/`
- [ ] T002 [P] Add document upload/scanner/storage settings and environment scaffolding in `ContosoDashboard/Program.cs` and `ContosoDashboard/appsettings*.json`
- [ ] T003 [P] Scaffold the document feature folders and file layout under `ContosoDashboard/Models/`, `ContosoDashboard/Services/`, `ContosoDashboard/Endpoints/`, and `ContosoDashboard/Pages/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Create the common document model, storage, scanner, and authorization foundation shared by every story.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T004 Add `Document`, `DocumentTag`, `DocumentShare`, and `DocumentActivity` models plus EF Core configuration in `ContosoDashboard/Models/` and `ContosoDashboard/Data/ApplicationDbContext.cs`
- [ ] T005 Implement durable upload storage contracts and local quarantine workflow in `ContosoDashboard/Services/IFileStorageService.cs` and `ContosoDashboard/Services/LocalFileStorageService.cs`
- [ ] T006 Implement the scanner boundary and fail-closed ClamAV adapter in `ContosoDashboard/Services/IFileScanner.cs` and `ContosoDashboard/Services/ClamAvFileScanner.cs`
- [ ] T007 Implement shared authorization predicates and document query filters in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T008 Add LocalDB schema evolution and initialization safeguards in `ContosoDashboard/Program.cs` and `ContosoDashboard/Data/ApplicationDbContext.cs`
- [ ] T009 [P] Add activity-record scaffolding and notification/report hooks in `ContosoDashboard/Models/DocumentActivity.cs`, `ContosoDashboard/Services/DocumentService.cs`, and the existing notification integration points

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel.

---

## Phase 3: User Story 1 - Upload and Secure Documents (Priority: P1) 🎯 MVP

**Goal**: Allow authenticated employees to upload and secure work documents while rejecting invalid, infected, or over-limit files before they become usable.

**Independent Test**: Upload a supported file with valid metadata, verify it appears in My Documents, and confirm unauthorized users cannot find, preview, or download it.

### Tests for User Story 1

- [ ] T010 [P] [US1] Add upload validation and authorization tests in `ContosoDashboard.Tests/Services/DocumentServiceUploadTests.cs`
- [ ] T011 [P] [US1] Add upload endpoint tests for clean, invalid, infected, and unavailable-scan outcomes in `ContosoDashboard.Tests/Endpoints/DocumentUploadEndpointTests.cs`

### Implementation for User Story 1

- [ ] T012 [US1] Implement upload validation, quarantine-to-scan-to-publish flow, and per-file success/failure results in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T013 [US1] Implement `POST /api/documents/upload` with authenticated identity, project/task validation, and safe result payloads in `ContosoDashboard/Endpoints/DocumentFileEndpoints.cs`
- [ ] T014 [P] [US1] Add accessible upload UI and per-file result handling in `ContosoDashboard/Pages/Documents.razor` and the existing dashboard/project/task pages under `ContosoDashboard/Pages/`
- [ ] T015 [P] [US1] Enforce current-user, project, task, and share authorization on document creation and file publication in `ContosoDashboard/Services/DocumentService.cs` and `ContosoDashboard/Models/Document.cs`

**Checkpoint**: At this point, User Story 1 should be fully functional and testable independently.

---

## Phase 4: User Story 2 - Find and Use Documents (Priority: P2)

**Goal**: Let authorized users locate documents by search, filter, and sort, then preview or download only the files they can access.

**Independent Test**: Prepare documents with varied titles, categories, dates, projects, tags, and uploaders; verify only authorized matches appear, and preview/download works for eligible content.

### Tests for User Story 2

- [ ] T016 [P] [US2] Add list/search/sort authorization tests in `ContosoDashboard.Tests/Services/DocumentSearchTests.cs`
- [ ] T017 [P] [US2] Add preview/download endpoint tests for authorized and denied requests in `ContosoDashboard.Tests/Endpoints/DocumentAccessEndpointTests.cs`

### Implementation for User Story 2

- [ ] T018 [US2] Implement scoped list/search/sort queries and access filtering in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T019 [US2] Implement authorized preview/download endpoints with safe response headers and audit recording in `ContosoDashboard/Endpoints/DocumentFileEndpoints.cs`
- [ ] T020 [P] [US2] Add `My Documents`, `Shared with Me`, search, filter, and sort UI in `ContosoDashboard/Pages/Documents.razor`
- [ ] T021 [P] [US2] Wire project/task document visibility into the relevant existing pages under `ContosoDashboard/Pages/` and associated project/task services

**Checkpoint**: At this point, User Stories 1 and 2 should both work independently.

---

## Phase 5: User Story 3 - Maintain and Share Documents (Priority: P3)

**Goal**: Allow owners and authorized managers to update metadata, replace files, share access, and remove documents with validation and notification events.

**Independent Test**: Upload a document, edit metadata, replace the file, share it to a user or department, and delete it after confirmation while denying unauthorized attempts.

### Tests for User Story 3

- [ ] T022 [P] [US3] Add edit/share/delete authorization tests in `ContosoDashboard.Tests/Services/DocumentLifecycleTests.cs`
- [ ] T023 [P] [US3] Add replacement and share-grant validation tests in `ContosoDashboard.Tests/Services/DocumentShareTests.cs`

### Implementation for User Story 3

- [ ] T024 [US3] Implement metadata update, replacement, sharing, unsharing, and confirmation-gated delete workflow in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T025 [US3] Implement share and notification integration for user/department recipients in `ContosoDashboard/Services/DocumentService.cs` and the existing notification service code paths
- [ ] T026 [P] [US3] Add edit/replace/share/delete UI and confirmation flows in `ContosoDashboard/Pages/Documents.razor` and the existing project/task pages under `ContosoDashboard/Pages/`
- [ ] T027 [P] [US3] Enforce owner, team-lead, project-manager, and administrator permission checks plus activity records in `ContosoDashboard/Services/DocumentService.cs` and `ContosoDashboard/Models/DocumentActivity.cs`

**Checkpoint**: At this point, User Stories 1, 2, and 3 are independently functional.

---

## Phase 6: User Story 4 - Use Documents in Dashboard Workflows (Priority: P4)

**Goal**: Surface document activity in dashboard and project/task workflows and provide administrative reporting without exposing unauthorized information.

**Independent Test**: Confirm recent documents and counts reflect the current user, project/task attachment is respected, notifications fire for eligible members, and administrators see only authorized aggregate activity.

### Tests for User Story 4

- [ ] T028 [P] [US4] Add dashboard summary and report tests in `ContosoDashboard.Tests/Services/DashboardDocumentReportTests.cs`
- [ ] T029 [P] [US4] Add project/task document integration tests in `ContosoDashboard.Tests/Services/ProjectDocumentIntegrationTests.cs`

### Implementation for User Story 4

- [ ] T030 [US4] Implement recent-documents and visible-count dashboard summaries in `ContosoDashboard/Services/DashboardService.cs` and the dashboard UI under `ContosoDashboard/Pages/`
- [ ] T031 [US4] Implement task/project document association, notification triggers, and access-scoped project views in `ContosoDashboard/Services/DocumentService.cs` and the existing project/task services/pages under `ContosoDashboard/Pages/`
- [ ] T032 [P] [US4] Implement admin report aggregation and permission checks in `ContosoDashboard/Services/DocumentService.cs` and the admin/report pages under `ContosoDashboard/Pages/`
- [ ] T033 [P] [US4] Validate dashboard and notification scopes remain limited to authorized users in `ContosoDashboard/Pages/` and `ContosoDashboard/Services/`

**Checkpoint**: All user stories should now be independently functional and ready for final validation.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, security hardening, performance review, and documentation alignment across all stories.

- [ ] T034 [P] Run the end-to-end quickstart checks from `specs/001-document-upload-management/quickstart.md` and record any local ClamAV or LocalDB prerequisites for the training host
- [ ] T035 [P] Review the cross-story authorization, file-cleanup, and audit boundaries across `ContosoDashboard/Data/ApplicationDbContext.cs`, `ContosoDashboard/Services/DocumentService.cs`, and `ContosoDashboard/Endpoints/DocumentFileEndpoints.cs`
- [ ] T036 [P] Finalize feature documentation and scope notes in `specs/001-document-upload-management/` and the relevant repository docs, keeping the training-only/local-only framing explicit

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion and must be complete before any user story begins.
- **User Stories (Phase 3-6)**: Each story depends on the Foundational phase and can begin once the shared document contracts are in place.
- **Polish (Phase 7)**: Depends on all desired stories being complete.

### User Story Dependencies

- **User Story 1 (P1)**: MVP gate; it defines the upload/security foundation used by the rest of the feature.
- **User Story 2 (P2)**: Depends on the US1 upload/security contract and can run in parallel with US3/US4 after US1 is stable.
- **User Story 3 (P3)**: Depends on the US1 secure document lifecycle and can run in parallel with US2/US4 after US1 is stable.
- **User Story 4 (P4)**: Depends on the document storage/auth workflow from US1 and can run in parallel with US2/US3 after US1 is stable.

### Within Each User Story

- Tests (if included) should be written and ready before implementation.
- Models/service boundaries before endpoints/UI integration.
- Storage/scanner and permissions before user-facing flows.
- Story complete before moving to the next priority, but implementation can overlap across stories after US1.

### Parallel Opportunities

- Setup tasks T001-T003 can run in parallel.
- Foundational tasks T005-T009 can run in parallel once the model and storage contracts are scoped.
- US1 tests T010-T011 can run in parallel; US1 implementation T014-T015 can run in parallel with the endpoint/service work.
- US2 and US3 and US4 can proceed in parallel after US1, with work split by service, endpoint, and page files.

### Parallel Example: User Story 1

```bash
# Launch the upload validation and endpoint tests together
Task: "Add upload validation and authorization tests in ContosoDashboard.Tests/Services/DocumentServiceUploadTests.cs"
Task: "Add upload endpoint tests for clean, invalid, infected, and unavailable-scan outcomes in ContosoDashboard.Tests/Endpoints/DocumentUploadEndpointTests.cs"

# Parallelize the upload pages and service work once the contract is defined
Task: "Add accessible upload UI and per-file result handling in ContosoDashboard/Pages/Documents.razor"
Task: "Implement upload validation, quarantine-to-scan-to-publish flow, and per-file success/failure results in ContosoDashboard/Services/DocumentService.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational document model/storage/scanner/authorization foundation.
3. Implement only US1 document upload, validation, and secure publication in `ContosoDashboard/Services/DocumentService.cs` and `ContosoDashboard/Endpoints/DocumentFileEndpoints.cs`.
4. Verify upload success and rejection flows against the independent test in `specs/001-document-upload-management/spec.md` before proceeding.

### Incremental Delivery

1. Add US2 search/filter/preview/download once the upload contract is stable.
2. Add US3 metadata sharing lifecycle once retrieval is proven.
3. Add US4 dashboard and admin reporting once the core document service is stable and access rules are verified.
4. Finish with the cross-cutting polish and validation tasks from Phase 7.

---

## Notes

- This feature remains intentionally local/offline; no cloud storage or external provider is planned.
- The LocalDB + ClamAV host requirements are a prerequisite for any successful upload flow and must remain documented as operational limits for the training environment.
- The checked-out branch is `master`, and the selected feature is `specs/001-document-upload-management` from `.specify/feature.json`; the standard setup script currently resolves to `specs/master` and must not be used as the source of truth for this feature.
