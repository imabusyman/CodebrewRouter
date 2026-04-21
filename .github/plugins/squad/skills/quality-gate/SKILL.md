---
name: quality-gate
description: "Enforce the -warnaserror build + 95% coverage test gate on every squad [DONE]. Use this before any specialist (Coder, Tester, Reviewer, Infra) marks a phase complete."
---

# Quality gate â€” build + test + coverage

Every squad phase that produces code must pass this gate before `[DONE]`.

## Commands

```powershell
dotnet build --no-incremental -warnaserror
dotnet test --no-build --collect:"XPlat Code Coverage"
```

Both must succeed. If either fails, emit `[BLOCKED]` with the failure summary and the failing path.

## Rules

- **No `#nullable disable`.** If the compiler emits a nullable warning, fix the underlying null-handling.
- **No `#pragma warning disable`.** Silenced warnings are a CRITICAL review finding.
- **Zero tolerance for `CS8600`â€“`CS8629`** (nullable reference-type warnings).

## Coverage threshold

95% line coverage on the production files listed in the envelope's "Files you may edit" block. Compute from:

```
TestResults/<guid>/coverage.cobertura.xml
```

Grep for `<class name="<fully-qualified>"` blocks and compute `line-rate` per file. Line rate < 0.95 on any envelope-listed file â†’ `[BLOCKED]` with the file + percentage.

## When this skill applies

- **Coder** â€” after every compilable intermediate state, run the build. Test gate is deferred to Tester but coverage pre-check is OK.
- **Tester** â€” must pass build + test + coverage before `[DONE]`.
- **Reviewer** â€” runs this gate independently as part of clean-context review. A failure here is `[BLOCKED]` regardless of what Tester said.
- **Infra** â€” after any AppHost edit, confirm `dotnet build --no-incremental -warnaserror` is green before `[CHECKPOINT]` or `[DONE]`.

## Targeted runs (fast feedback)

Use `FullyQualifiedName~` filters for inner-loop iteration:

```powershell
dotnet test --no-build --filter "FullyQualifiedName~<YourNewTestClass>"
```

Once targeted passes, run the full suite + coverage for the gate.
