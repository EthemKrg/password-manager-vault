# Password Manager — WORKFLOW_RULES

## Purpose

This document defines how planning, implementation, review, and manual testing are handled.

## Core Rule

```text
Codex owns implementation in this workspace.
Project decisions are recorded in Markdown.
Security-sensitive changes are reviewed deliberately.
Manual testing is performed locally when needed.
```

The project should favor direct progress over task-document ceremony.

## Roles

### Codex

Responsible for reading the repository, updating project documents when decisions change, implementing code, running feasible checks, and reporting concrete results.

Codex may make normal engineering decisions that follow the existing documents. Security-sensitive or product-shaping decisions must be written into the relevant Markdown file.

### User

Provides product preference, local manual testing feedback, and final approval for major product/security changes when needed.

### External Reviewer

Optional. Use only for large or risky work where a second eye is worth the overhead, such as vault, sync, backup, encryption-related behavior, large refactors, architecture changes, or broad UI/system changes.

An external reviewer is a second eye only. The project documents remain the source of truth.

### User Manual Testing

Manual tests are performed locally. Codex provides short focused test steps when needed.

Avoid long manual test scripts unless explicitly requested.

## Task Routing

### Small Fix

Examples: missing import, one-line build error, simple null check, minor XAML binding fix, tiny config edit, typo/rename.

Routing:

```text
Codex fixes directly.
```

### Medium Task

Examples: contained feature, new service class, small screen, localized refactor, basic tests.

Routing:

```text
Codex implements directly, then runs focused verification.
```

### Large / Risky Task

Examples: vault, encryption, backup/versioning, sync/conflict handling, large refactor, architecture-sensitive work, security-sensitive work.

Routing:

```text
Codex records the plan or decision in Markdown.
Codex implements in small verifiable steps.
External review may be used when useful.
User approval is required for product/security direction changes.
```

## Manual Test Rule

Manual tests should be short and focused.

Example:

```text
1. Open the app.
2. Create a new vault.
3. Add one test entry.
4. Close and reopen the app.
5. Unlock with the correct master password.
6. Verify the entry exists.
7. Try wrong master password.
8. Confirm it fails safely.
```

## Locked Workflow Decision

```text
Planning: Codex + User through project Markdown
Implementation: Codex directly in this workspace
Second-eye review: Optional for large/risky work
Small fixes: Codex handles directly
Manual testing: User performs when required, Codex gives short steps
Decision authority: Project documents + User approval for major changes
Efficiency: Required
```
