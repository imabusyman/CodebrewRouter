# DevUI agents

This folder is discovered by the Microsoft Agent Framework DevUI (`devui` CLI)
when AppHost launches it. Each subfolder is one agent; `__init__.py` must
export a variable named `agent` (or `workflow`).

The AppHost passes this directory to `devui` and injects `OPENAI_BASE_URL`
pointing at the running gateway plus a placeholder `OPENAI_API_KEY`, so the
agents here can chat through the gateway without manual configuration.

Prereq on the host machine: `pip install agent-framework-devui` (this also
pulls in `agent-framework` and `agent-framework-openai`).
