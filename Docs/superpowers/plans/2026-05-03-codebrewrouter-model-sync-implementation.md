# CodebrewRouter Model Sync & Router Failover Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable CodebrewRouter to route requests through synchronized Ollama routers (.12 primary, .53 backup) with gemma4:e4b, execute on LM Studio worker (.56), and integrate with Microsoft Agent Framework.

**Architecture:** Three-tier system with thread-safe dynamic failover. Router tier classifies requests (5-step pipeline: cleanup → count → classify → route → execute), worker tier executes. Model sync validation on startup, health-check-driven failover on request failure.

**Tech Stack:** .NET 10, Microsoft.Extensions.AI (MEAI), Ollama (gemma4:e4b), LM Studio (qwen3.6-27b), xUnit tests, Aspire orchestration

---

## File Structure

### New Files (to create)
- `Blaze.LlmGateway.Infrastructure/HealthManagement/IOllamaHealthState.cs`
- `Blaze.LlmGateway.Infrastructure/HealthManagement/OllamaHealthStateService.cs`
- `Blaze.LlmGateway.Infrastructure/Validation/IOllamaModelSyncValidator.cs`
- `Blaze.LlmGateway.Infrastructure/Validation/OllamaModelSyncValidator.cs`
- `Blaze.LlmGateway.Tests/HealthManagement/OllamaHealthStateTests.cs`
- `Blaze.LlmGateway.Tests/Validation/ModelSyncValidationTests.cs`
- `Docs/SETUP_OLLAMA_ROUTERS.md`

### Files to Modify
- `Blaze.LlmGateway.Core/Configuration/LlmGatewayOptions.cs`
- `Blaze.LlmGateway.Api/appsettings.json`
- `Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs`
- `Blaze.LlmGateway.Infrastructure/PromptCleaning/GemmaPromptCleaner.cs`
- `Blaze.LlmGateway.Infrastructure/TaskClassification/OllamaTaskClassifier.cs`
- `Blaze.LlmGateway.Api/Program.cs`
- `CLAUDE.md`

---

## Task List (17 tasks across 8 phases)

See tasks 1-17 in full document (sections below).

---

## Execution Options

**Plan is comprehensive, saved to:** `Docs/superpowers/plans/2026-05-03-codebrewrouter-model-sync-implementation.md`

**Two execution approaches:**

1. **Subagent-Driven (recommended)**  
   Dispatch fresh subagent per task, review between tasks, faster iteration

2. **Inline Execution**  
   Execute in this session using executing-plans skill, batch with checkpoints

Which would you prefer?
