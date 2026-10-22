<!--
Sync Impact Report
Version change: unratified template -> 1.0.0
Modified principles: none -> Training-First Scope; Security and Privacy by Default;
  Maintainable, Layered Design; Behavior Verified by Focused Checks;
  Accessible, Consistent User Experience
Added sections: Project Constraints; Development Workflow
Removed sections: template examples and placeholder guidance
Follow-up TODO: Record the original ratification date when known.
-->

# ContosoDashboard Constitution

## Core Principles

### I. Training-First Scope
ContosoDashboard MUST remain clearly identified as a fictional training application.
Documentation and user-facing guidance MUST distinguish training examples from
production-ready capabilities. Changes MUST NOT imply that mock authentication,
sample data, or simplified infrastructure are suitable for production deployment.

### II. Security and Privacy by Default
Protected pages and data operations MUST enforce authentication and authorization at
the server or service boundary; hiding a control in the UI is not an authorization
check. Data access MUST be scoped to the requesting user's permitted records.
Changes MUST avoid exposing secrets or sensitive data in logs, errors, and UI output.
Security-sensitive changes MUST include focused checks of both permitted and denied
access where practical.

### III. Maintainable, Layered Design
Changes MUST preserve clear separation between UI, business services, data models,
and persistence. New behavior MUST use existing service abstractions and dependency
injection patterns where applicable. Changes MUST be limited to the requested scope
and avoid unrelated refactoring; deviations from the existing architecture require
a clear justification.

### IV. Behavior Verified by Focused Checks
Each change MUST be validated with the smallest relevant automated test, build, or
other repeatable check available. New or changed behavior MUST receive automated
coverage when the repository's test setup supports it; otherwise, the limitation
and the manual verification performed MUST be stated. Validation results MUST not
be represented as broader than the checks actually run.

### V. Accessible, Consistent User Experience
Interactive UI MUST use semantic controls with accessible names and labels, preserve
keyboard access, and present validation or errors in a perceivable way. Status and
priority MUST NOT be communicated by color alone. New UI MUST follow the existing
navigation, layout, and visual conventions unless the feature explicitly changes
them.

## Project Constraints

The application uses ASP.NET Core 8, Blazor Server, Entity Framework Core, and
SQL Server LocalDB. Changes MUST remain compatible with this stack unless an
approved feature plan explicitly changes it. The application is intended to work
locally for training and currently has no cloud-service dependency. Mock sign-in,
database initialization, and other simplified implementations MUST be documented
as training limitations, not production assurances.

## Development Workflow

Feature work SHOULD follow the Spec Kit sequence: specify user needs and acceptance
criteria, plan the implementation, create dependency-ordered tasks, then implement
and validate. Specifications and plans MUST remain consistent with this constitution.
Reviews MUST check the changed behavior against its requirements and the principles
above. Known limitations or deferred work MUST be recorded in the relevant feature
artifacts or review summary.

## Governance

This constitution governs feature specifications, implementation plans, code
changes, and reviews for this repository. Amendments require maintainer review and
MUST update the version and last-amended date. Versioning follows semantic intent:
MAJOR for removing or redefining a principle, MINOR for adding a principle or
materially expanding governance, and PATCH for clarifications that do not change
requirements. Reviewers MUST identify and document any justified deviation rather
than silently waiving a principle.

**Version**: 1.0.0 | **Ratified**: TODO(RATIFICATION_DATE): original date unknown | **Last Amended**: 2026-10-22
