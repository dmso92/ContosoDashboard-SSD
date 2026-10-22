# Document Management Contract

## Browser-facing experience

The document feature is a capability of the existing authenticated Blazor Server application, not a separate public API.

| Surface | Contract |
|---|---|
| Documents navigation | Authenticated users can open My Documents, Shared with Me, search, filter, sort, and initiate actions allowed by server authorization. |
| My Documents | Show only authorized documents uploaded by the current user; display title, category, upload date, file size, and project when present. |
| Project/task panels | Show only documents the current user may access in the current project/task context. Uploading from a task associates the document with that task and its project. |
| Dashboard | Recent Documents is limited to the current user's five most recent uploads; document count includes only documents visible to that user. |
| Administrator reports | Only administrators may view aggregate document type, uploader, and access-activity reports. |
| Interaction and accessibility | Semantic labels on controls; keyboard-accessible file selection, dialogs, and actions; per-file progress and outcomes announced/perceivable without relying on color alone. Destructive deletion requires explicit confirmation. |

The UI may hide unavailable actions for usability, but that is not an authorization boundary. Every query and operation must authorize the current user on the server.

## File transfer endpoints

The application exposes browser-only authenticated endpoints for transfer. Endpoint routing is an implementation detail that must remain consistent with this contract.

### `POST /api/documents/upload`

- **Authentication**: Existing authenticated cookie identity; apply the application's cookie/CSRF protections for state-changing requests.
- **Request**: One `multipart/form-data` file and its own required title/category, plus optional description, tags, project ID, and task ID.
- **Validation**: Enforce the supported format allowlist and inclusive 25 MiB per-file limit server-side. Resolve uploader from authenticated identity; ignore/reject any client-supplied uploader or storage path.
- **Processing**: Stage outside `wwwroot`, scan locally, and only publish a clean file. A task link must resolve to a task the user may access and its project association is derived/validated server-side.
- **Success**: Return the created document's safe metadata/identifier and an individual success result.
- **Failure**: Return a perceivable per-file failure result for invalid type/size, denied project/task access, infected content, or unavailable scan. A failed or unscanned file is never available.
- **Batch behavior**: The browser submits each selected file independently so one failure does not suppress other outcomes. Progress and result are tracked per file.

### `GET /api/documents/{id}/download`

- Reauthorize the current user against current owner, role, project/team membership, and direct-share rules for every request.
- Record a download event only after authorization; unauthorized attempts are denied and recorded without disclosing document metadata or contents.
- Stream content from local storage with attachment disposition and server-validated content type. Never redirect to a raw path or expose a filesystem location.

### `GET /api/documents/{id}/preview`

- Apply the same per-request authorization and audit policy as download.
- Permit inline delivery only for supported PDF and image content. Other supported formats return a controlled unsupported-preview response and remain downloadable.
- Set safe response headers, including content-type and `X-Content-Type-Options: nosniff`; do not permit arbitrary user-selected MIME types to control execution.

## In-app service operations

The Blazor UI calls server-side services for listing/search, metadata updates, replacement, sharing/unsharing, deletion, dashboard summaries, and reports. Each operation derives the actor from authenticated context and enforces these rules:

- Owner: view, edit metadata, replace, share, delete own document.
- Current project member: view/download/preview associated project documents.
- Team lead: view, edit metadata, and replace documents uploaded by users in the lead's current department; no deletion authority over another user's document.
- Project manager: manage documents associated with projects they manage, including deletion.
- Administrator: access and manage all documents and view reports.
- Shared recipient: access while a direct-user grant remains active, or while current department membership matches a department share; project membership and administrator rules are always evaluated from current records.

Search, facets, sort, list counts, and dashboard summaries must operate only on rows authorized for the requester. User-facing errors must not reveal whether an inaccessible document identifier exists.
