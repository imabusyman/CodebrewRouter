---
name: 'Tool Guardian'
description: 'Blocks dangerous tool operations (destructive file ops, force pushes, DB drops) before the Copilot coding agent executes them'
tags: ['security', 'safety', 'preToolUse', 'guardrails']
---

# Tool Guardian Hook

Blocks dangerous tool operations before a GitHub Copilot coding agent executes them, acting as a safety net against destructive commands, force pushes, database drops, and other high-risk actions.

## Overview

AI coding agents can autonomously execute shell commands, file operations, and database queries. Without guardrails, a misinterpreted instruction could lead to irreversible damage. This hook intercepts every tool invocation at the `preToolUse` event and scans it against threat patterns across multiple categories.
