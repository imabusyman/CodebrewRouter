---
applyTo: "**/*.cs"
---

# Code style conventions

Apply to: every C# source file in the solution.

## Language features

- **Primary constructors** for types that take dependencies. No manual backing fields for DI.
  ```csharp
  public sealed class LlmRoutingChatClient(
      IServiceProvider services,
      IRoutingStrategy strategy,
      ILogger<LlmRoutingChatClient> logger) : DelegatingChatClient(NullChatClient.Instance) { ... }
  ```
- **Collection expressions** `[]` over `new List<T>()` / `Array.Empty<T>()` where idiomatic.
- **Target-typed `new()`** where the type is obvious from context.
- **File-scoped namespaces** — no indented namespace blocks.
- **Top-level statements** in `Program.cs`.

## Nullability

- Nullable reference types enabled at the project level.
- No `#nullable disable` file-scopes.
- No `#pragma warning disable CS8600`-`CS8629`.
- If the compiler flags a warning, fix the underlying null-handling — never silence it.

## Async + cancellation

- `async Task` / `async ValueTask` everywhere I/O happens.
- `CancellationToken` is the last parameter and is propagated into every downstream call.
- No `.Wait()`, `.Result`, or `.GetAwaiter().GetResult()` anywhere.
- Use `await foreach` for `IAsyncEnumerable<T>`.

## Types

- `record` / `record struct` for immutable data: config, state snapshots, DTOs.
- `sealed` by default on new classes unless designed for inheritance.
- `readonly` fields; prefer `init`-only properties over full setters.

## Logging

- `ILogger<T>` injected via constructor; never `LoggerFactory.CreateLogger()`.
- Structured logging with placeholders (`{Provider}`, `{Duration}`), not string interpolation.
- Include provider name, error type, and duration at every failure point.

## Build gate

```powershell
dotnet build --no-incremental -warnaserror
```

Zero tolerance for:
- `CS8600`–`CS8629` nullable warnings.
- Unused usings.
- Unused variables.
- Unused private methods.
