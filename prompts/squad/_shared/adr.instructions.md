---
applyTo: "Docs/design/adr/**"
---

# ADR authoring conventions

Apply to: every file under `Docs/design/adr/`.

## Numbering

- Next ADR number is `max(existing) + 1`. At time of writing, ADR-0009 is the highest — next new ADR is ADR-0010.
- File name: `NNNN-<kebab-case-title>.md`. Title matches the ADR heading.

## Template

Use `0000-adr-template.md` verbatim as a skeleton. Required sections in order:

1. **Header** — title line `# ADR-NNNN: <imperative title>`.
2. **Status** — starts `Proposed`. Moves to `Accepted` after review. Use `Superseded by ADR-NNNN` when retired.
3. **Date** — ISO `YYYY-MM-DD`.
4. **Deciders** — roles, not people.
5. **Related** — cross-links to prior ADRs, PRD sections, plan docs.
6. **Context** — forces at play; why now.
7. **Decision** — one declarative sentence (`"We will ..."`).
8. **Details** — schema, interface shapes, config keys, file paths, migration path. Engineer-ready.
9. **Consequences** — Positive / Negative / Neutral, each as bullets.
10. **Alternatives Considered** — at minimum **two** rejected alternatives with reasons. Label Alternative A / B / ...
11. **References** — tech-design §, research docs, related ADRs.

## Cross-linking rules

- When you author ADR-N, update the "Related" section of every ADR it builds on or contradicts.
- When you supersede ADR-M, flip its status to `Superseded by ADR-N` and add a pointer back.

## Style

- No em-dashes inside link text. Markdown links must be relative (e.g. `../../plan/llm-agent-platform-plan.md`).
- Headings use `##` / `###`; never skip a level.
- Code fences declare a language (` ```csharp `, ` ```json `, ` ```powershell `).
- Tables for enumerated things (error categories, SDK mappings, provider matrices).

## Hard rules

- Never mark a new ADR `Accepted` on first authoring — Status `Proposed` always.
- Never author an ADR without at least two Alternatives Considered.
- Never touch ADRs 0001–0008 (foundational decisions); supersede rather than rewrite.
- Never introduce ADRs whose Decision contradicts ADR-0008 (default-deny cloud egress) without an explicit amendment.
