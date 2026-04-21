---
name: prove-it-bugfix
description: "DevSquad-style bug-fix flow — reproduce, write a failing test, fix, verify. Use this whenever the squad task is \"fix bug X\" or the Planner flags a step as a bug repro."
---

# Prove-It — bug-fix flow

When the task is to fix a bug, the squad proves the bug before the fix, then proves the fix with the same test.

## Flow

1. **Reproduce.** Read the failing code path. Reconstruct the exact inputs that trigger the failure.
2. **Red.** Tester writes the smallest possible failing test that captures the bug. Test MUST fail against the pre-fix baseline.
3. **Root cause.** Coder identifies the compiler / runtime / logic reason the test fails.
4. **Green.** Coder makes the minimal change that flips the test from red to green. No scope creep.
5. **Gate.**
   ```powershell
   dotnet build --no-incremental -warnaserror
   dotnet test --no-build --collect:"XPlat Code Coverage"
   ```
6. **Prove-It check.** If the new test passes against BOTH the pre-fix and post-fix code, it's insufficient — tighten the assertion or narrow the inputs until it truly distinguishes the two states.

## Roles in the flow

| Phase | Owner | Output |
|---|---|---|
| Reproduce | Planner | `spec.md` §"Current state" with the failing code path cited |
| Red | Tester | `<Area>/<Class>Tests.cs` with `[Fact]` that fails against baseline |
| Root cause | Coder | Log entry (MEDIUM): "Failure reason: <compiler | runtime | logic>" |
| Green | Coder | Minimal diff on the bug site |
| Gate | Coder (build) → Tester (test + coverage) | `[CHECKPOINT]` then `[DONE]` |
| Verify | Reviewer | Reread diff, confirm fix is minimal, no new CRITICAL/HIGH findings |

## Anti-patterns (reject)

- Adding a test that asserts the fixed behavior but would have also passed against the bug. Not a proof.
- Expanding the fix to "while I'm here" scope. Every new file touched is a new envelope — route back to the Conductor.
- Silencing the compiler / test instead of fixing. `#nullable disable` or `Assert.True(true)` in place of real assertions → CRITICAL.

## Reporting

Prove-It runs append one reasoning-log entry per phase flip:

```markdown
## <ts> — tester — HIGH
Decision: captured <bug-name> with assertion on <invariant>
Rationale: test fails against commit <sha> (pre-fix); passes against current HEAD
Evidence: Blaze.LlmGateway.Tests/<Area>/<Class>Tests.cs:<line>
```
