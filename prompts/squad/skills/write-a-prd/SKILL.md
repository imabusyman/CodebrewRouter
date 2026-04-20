---
name: write-a-prd
description: Generate a Product Requirements Document from a task description, user intent, or PRD outline. Structure the PRD with sections: Overview, Problem Statement, Goals, Scope, Features, Acceptance Criteria, Metrics. Use this when the Planner or Orchestrator needs to formalize requirements before decomposing into implementation steps.
---

# Write a PRD — generate a Product Requirements Document

Formalizes task intent into a structured PRD that serves as input to the Planner or Orchestrator.

## When to use

- **Planner phase:** Before decomposing a task into steps, generate a PRD to align on requirements.
- **Orchestrator setup:** Before running the autonomous loop, generate/refine a PRD.
- **Specification clarity:** Task description is vague; PRD forces clarity on scope + acceptance criteria.

## Template

A PRD for Blaze.LlmGateway tasks typically has:

```markdown
# PRD: <Feature Name>

## Overview
<One-paragraph executive summary: what, why, who benefits>

## Problem Statement
<Current state, pain point, why it matters>

## Goals
- <Specific, measurable goal>
- <Specific, measurable goal>

## Scope
### In scope
- <Feature or component>
- <Behavior or interface>

### Out of scope
- <Deliberate exclusion>

## Features
### Feature 1: <Name>
<Description, inputs, outputs, constraints>

### Feature 2: <Name>
<Description, inputs, outputs, constraints>

## Acceptance Criteria
- [ ] Criterion 1 (testable, specific)
- [ ] Criterion 2 (testable, specific)
- [ ] Blaze.LlmGateway-specific: builds with `-warnaserror`, 95% code coverage, ADR-0008 compliant

## Success Metrics
- <Quantifiable outcome: latency, coverage, adoption>

## Dependencies
- <Existing components or external services this relies on>

## Open Questions
- <Ambiguities to resolve before implementation>
```

## Output location

- **Primary:** `Docs/PRD/<slug>.md` (long-lived, blessed version).
- **Per-run copy (optional):** `Docs/squad/runs/<ts>-<slug>/prd.md` (working copy for this run).

## Examples from Blaze.LlmGateway

See `Docs/PRD/blaze-llmgateway-prd.md` for a system-level PRD. Task-specific PRDs are smaller and focused on a single feature or component (e.g., "add a circuit breaker to LlmRoutingChatClient").

## Quality checks

Before marking the PRD complete:

- **Acceptance criteria are testable** (not vague).
- **Scope is clear** (in/out sections are disjoint and complete).
- **No missing context** (team knows what "done" looks like).
- **Blaze.LlmGateway constraints** are listed (MEAI usage, quality gate, ADRs).

## See also

- `squad-plan` command — invoked after PRD is approved, decomposes into ordered steps.
- Planner agent — reads the PRD and generates spec.md + plan.md.
