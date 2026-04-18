---
description: 'Guidelines for building safe, governed AI agent systems. Apply when writing code that uses agent frameworks, tool-calling LLMs, or multi-agent orchestration to ensure proper safety boundaries, policy enforcement, and auditability.'
applyTo: '**'
---

# Agent Safety & Governance

## Core Principles

- **Fail closed**: If a governance check errors or is ambiguous, deny the action rather than allowing it
- **Policy as configuration**: Define governance rules in YAML/JSON files, not hardcoded in application logic
- **Least privilege**: Agents should have the minimum tool access needed for their task
- **Append-only audit**: Never modify or delete audit trail entries — immutability enables compliance

## Tool Access Controls

- Always define an explicit allowlist of tools an agent can use — never give unrestricted tool access
- Separate tool registration from tool authorization — the framework knows what tools exist, the policy controls which are allowed
- Use blocklists for known-dangerous operations (shell execution, file deletion, database DDL)
- Require human-in-the-loop approval for high-impact tools (send email, deploy, delete records)
- Enforce rate limits on tool calls per request to prevent infinite loops and resource exhaustion
