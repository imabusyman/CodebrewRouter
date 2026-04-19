# LiteLLM Provider Registration Research

Repository context: [BerriAI/litellm](https://github.com/BerriAI/litellm)

## Executive Summary

LiteLLM's `provider_registration` guide is mainly for adding a brand-new adapter, not for wiring up the providers on your shortlist, because most of the backends you want already exist as first-class provider routes or can be handled through LiteLLM's OpenAI-compatible path.[^1][^2][^3] The current provider list already includes `litellm_proxy`, `ollama`, `ollama_chat`, `openrouter`, `azure_ai`, `gemini`, `anthropic`, and `github_copilot`, which means your initial implementation can stay focused on configuration and model mapping instead of authoring new provider classes.[^4][^5][^6][^7][^8][^9][^10][^11]

For your stated scope, the cleanest policy is: use `litellm_proxy` when chaining to another LiteLLM proxy; use `ollama` / `ollama_chat` for local or cloud Ollama; use `openrouter` for OpenRouter; use `azure_ai` for Microsoft Foundry-hosted models; use `gemini` for Gemini API-key access; use `anthropic` for Claude API-key access; and use `github_copilot` for GitHub Copilot's device-flow-authenticated chat API.[^5][^6][^7][^8][^9][^10][^11] I found no first-class `gemini_cli`, `claude_code`, or `ollama_cloud` provider names in the current provider list, so those should be modeled as either existing providers (`gemini`, `anthropic`, `ollama`) or generic OpenAI-compatible upstreams rather than as new LiteLLM route prefixes.[^4][^12]

The practical consequence for CodebrewRouter is that you probably do **not** want to implement new LiteLLM-style provider adapters for this scope. Instead, you want a constrained provider catalog that normalizes your friendly product names onto LiteLLM's existing provider prefixes and reserves the OpenAI-compatible path for special cases like Foundry Local or any other internal endpoint that speaks the OpenAI wire format.[^1][^2][^3][^11]

## What the `provider_registration` Guide Actually Means

LiteLLM's provider-registration guide describes the full adapter-authoring path: create a provider-specific `transformation.py`, add provider keys/imports, wire routing in `main.py`, add the provider to `LITELLM_CHAT_PROVIDERS`, extend provider-prefix detection in `get_llm_provider_logic.py`, update streaming behavior if needed, and add tests.[^1] That is the right path when your backend is a genuinely new protocol or request/response shape.[^1]

The same guide also explicitly says OpenAI-compatible providers are the easy path: they can be added via a single JSON file instead of a full handwritten adapter.[^1] The current resolver backs that up in code: `get_llm_provider_logic.py` checks `JSONProviderRegistry` before falling back to the enum-style provider list, which means OpenAI-compatible upstreams are intentionally treated as a lighter-weight integration path than native providers.[^3]

For your shortlist, that distinction matters because only a subset of your targets need a native LiteLLM provider route. Everything else should be routed to one of LiteLLM's existing built-ins or to the generic OpenAI-compatible path instead of spawning new provider modules.[^1][^2][^3]

## Recommended Mapping for Your Target Providers

| Desired backend | Recommended LiteLLM route | Auth / config | Recommendation |
|---|---|---|---|
| LiteLLM upstream proxy | `litellm_proxy/<model>` | `LITELLM_PROXY_API_BASE` / `LITELLM_PROXY_API_KEY`, or explicit `api_base` / `api_key` | Use the dedicated `litellm_proxy` route when the upstream is another LiteLLM proxy and you want LiteLLM's proxy-specific defaults and flags.[^4][^11] |
| Ollama local | `ollama_chat/<model>` for chat, `ollama/<model>` for generate/completions | `api_base=http://localhost:11434` or `OLLAMA_API_BASE`; optional `OLLAMA_API_KEY` | Use LiteLLM's native Ollama support; the docs explicitly recommend `ollama_chat` for better chat responses.[^4][^5][^13] |
| Ollama cloud | `ollama/<model>` | `OLLAMA_API_KEY` plus cloud base URL | There is no separate `ollama_cloud` provider; current resolver logic maps `ollama.com` endpoints to the `ollama` provider and reads `OLLAMA_API_KEY`.[^4][^13] |
| OpenRouter | `openrouter/<provider>/<model>` | `OPENROUTER_API_KEY`; optional `OPENROUTER_API_BASE`, `OR_SITE_URL`, `OR_APP_NAME` | Use the first-class `openrouter` route; LiteLLM documents it as supporting all OpenRouter models.[^4][^6] |
| Microsoft Foundry hosted | `azure_ai/<model>` | `AZURE_AI_API_KEY` + `AZURE_AI_API_BASE`, or Azure AD token for supported paths | Use `azure_ai` as the default Foundry/Azure AI route; LiteLLM documents it as the provider for Azure AI Studio / Foundry-style endpoints.[^4][^7] |
| Microsoft Foundry Claude hosted | `azure_ai/claude-*` | `AZURE_AI_API_KEY` + `AZURE_AI_API_BASE=https://<resource>.services.ai.azure.com/anthropic`, or Azure AD token | Prefer `azure_ai/claude-*`; LiteLLM explicitly recommends this path for Azure Foundry Claude because it preserves Azure support and advanced Claude features.[^7][^9] |
| Microsoft Foundry Local | `openai/<model>` or JSON-registered OpenAI-compatible provider | `api_base`, `api_key`, and `/v1`-style OpenAI-compatible base URL | Treat Foundry Local as an OpenAI-compatible integration unless it exposes a native Azure AI contract; that is exactly the lightweight path the registration guide and OpenAI-compatible docs are designed for.[^1][^2][^3] |
| GitHub Copilot CLI / subscription | `github_copilot/<model>` | OAuth device flow, stored locally by LiteLLM | Use the dedicated `github_copilot` provider; LiteLLM documents automatic authentication handling and a device-flow login, which makes this the one CLI/subscription-like target in your list that already has a first-class provider route.[^4][^10] |
| Gemini API key | `gemini/<model>` | `GEMINI_API_KEY` | Use `gemini/` when you want API-key auth; the docs explicitly contrast this with Vertex AI's heavier GCP credential flow.[^4][^8] |
| Gemini CLI | `gemini/<model>` | `GEMINI_API_KEY` | I would **not** model Gemini CLI as its own provider. The current provider list contains `gemini` but not `gemini_cli`, and the docs tell API-key users to use the `gemini/` prefix.[^4][^8][^12] |
| Claude API key | `anthropic/<model>` | `ANTHROPIC_API_KEY` | Use the first-class `anthropic` route for direct Claude access.[^4][^9] |
| Claude Code | `anthropic/<model>` or `azure_ai/claude-*` | `ANTHROPIC_API_KEY`, or Azure auth if hosted on Foundry | I would **not** model Claude Code as its own provider. The current provider list contains `anthropic` but not `claude_code`, and the documented Claude paths are `anthropic/*` or Azure Foundry Claude through `azure_ai/*`.[^4][^7][^9][^12] |

## Recommended Provider Policy for CodebrewRouter

If the goal is to recreate a LiteLLM-like provider layer inside your project without copying LiteLLM's full adapter ecosystem, I would constrain your initial provider vocabulary to the following user-facing set:

1. `litellm`
2. `ollama`
3. `openrouter`
4. `microsoft-foundry`
5. `microsoft-foundry-local`
6. `github-copilot`
7. `gemini`
8. `claude`

Under the hood, I would map those names to LiteLLM-style routes like this:

| Your friendly name | Internal route to emulate | Why |
|---|---|---|
| `litellm` | `litellm_proxy/<model>` | Dedicated upstream-proxy route already exists and has explicit defaulting logic.[^4][^11] |
| `ollama` | `ollama_chat/<model>` by default, `ollama/<model>` when non-chat semantics are needed | LiteLLM already distinguishes the better chat path from the raw generate path.[^4][^5] |
| `openrouter` | `openrouter/<provider>/<model>` | First-class provider with explicit env-var-based config and provider/model routing semantics.[^6] |
| `microsoft-foundry` | `azure_ai/<model>` | Best documented fit for Foundry/Azure AI hosted endpoints.[^7] |
| `microsoft-foundry-local` | `openai/<model>` or JSON-registered custom provider | Best fit when the local endpoint is OpenAI-compatible rather than a native Azure AI endpoint.[^1][^2][^3] |
| `github-copilot` | `github_copilot/<model>` | Dedicated provider with automatic device-flow auth and documented proxy support.[^4][^10] |
| `gemini` | `gemini/<model>` | API-key route is explicit and simpler than Vertex AI credentialing.[^8] |
| `claude` | `anthropic/<model>` or `azure_ai/claude-*` | Covers both direct Anthropic keys and Azure-hosted Claude on Foundry.[^7][^9] |

That policy keeps your product vocabulary stable while avoiding unnecessary provider proliferation.[^1][^4] It also gives you a clean place to reject out-of-scope inputs like `gemini-cli`, `claude-code`, or `ollama-cloud` as separate provider identifiers and normalize them onto the supported routes above instead.[^4][^12]

## What I Would **Not** Implement as Separate Providers

I would **not** add separate provider identifiers for `gemini_cli` or `claude_code`, because the current LiteLLM provider list enumerates `gemini` and `anthropic` but does not enumerate those CLI-branded variants.[^4] I also found provider docs for `gemini`, `anthropic`, `github_copilot`, `ollama`, `openrouter`, `azure_ai`, and `openai_compatible`, which reinforces that LiteLLM's documented mental model is "Gemini API", "Anthropic API", "GitHub Copilot API", "Ollama", "OpenRouter", "Azure AI/Foundry", and "OpenAI-compatible endpoints" rather than separate Gemini CLI or Claude Code provider classes.[^8][^9][^10][^12]

I would also **not** add a separate `ollama_cloud` provider, because the built-in route catalog only exposes `ollama` / `ollama_chat`, while the resolver's endpoint heuristics special-case `ollama.com` and pair it with `OLLAMA_API_KEY`.[^4][^13] That is strong evidence that LiteLLM treats local Ollama and cloud-hosted Ollama as deployment variants of the same provider, not as two different provider families.[^13]

## Practical Implementation Steps

1. **Start from configuration, not adapter authoring.** Your shortlist is already covered by LiteLLM-native routes plus the OpenAI-compatible path, so you do not need to recreate the full provider-registration flow unless you later add a truly new backend protocol.[^1][^2][^3]
2. **Reserve native routes for native providers.** Implement internal mappings for `litellm_proxy`, `ollama`/`ollama_chat`, `openrouter`, `azure_ai`, `gemini`, `anthropic`, and `github_copilot` first.[^4][^5][^6][^7][^8][^9][^10][^11]
3. **Use OpenAI-compatible integration for local/special endpoints.** Foundry Local and any similar internal gateway should go through `openai/<model>` or the JSON provider registry unless you discover they require a truly custom request/response adapter.[^1][^2][^3]
4. **Treat CLI-branded offerings as auth or packaging differences, not provider families.** GitHub Copilot is the exception because LiteLLM explicitly documents device-flow auth and a dedicated `github_copilot/` route; Gemini and Claude should stay on their API-native providers.[^8][^9][^10]
5. **Write tests at the routing layer.** The registration guide explicitly expects provider-specific test coverage, and your project should do the same for provider-name normalization plus credential resolution.[^1]

## Confidence Assessment

**High confidence**

- The provider-registration guide is about authoring new adapters, while OpenAI-compatible upstreams are intended to use a lighter JSON/openai-compatible path.[^1][^2][^3]
- LiteLLM currently exposes the exact built-in provider names you need for `litellm_proxy`, `ollama`, `ollama_chat`, `openrouter`, `azure_ai`, `gemini`, `anthropic`, and `github_copilot`.[^4]
- GitHub Copilot is a documented first-class provider with device-flow authentication, while Gemini API-key usage, Anthropic API-key usage, OpenRouter, Ollama, and Azure AI/Foundry are all documented as first-class integrations.[^5][^6][^7][^8][^9][^10]

**Moderate confidence / bounded inference**

- My recommendation to model **Foundry Local** through `openai/<model>` or the JSON provider registry is an engineering inference based on LiteLLM's OpenAI-compatible guidance and resolver behavior, not a docs page that names "Foundry Local" directly.[^1][^2][^3]
- My recommendation to treat **Gemini CLI** and **Claude Code** as non-provider concepts is also an inference from the current provider list and docs set: I found documented routes for `gemini`, `anthropic`, and `github_copilot`, but not for `gemini_cli` or `claude_code`.[^4][^8][^9][^10][^12]

## Footnotes

[^1]: `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\provider_registration_excerpt.md:5-80`
[^2]: `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\docs\providers\openai_compatible.md:8-25,68-85`
[^3]: `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\get_llm_provider_logic-current.py:172-200`
[^4]: `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\constants-current.py:518-608`
[^5]: `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\docs\providers\ollama.md:11-18,20-31,90-138`; `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\get_llm_provider_logic-current.py:622-626`
[^6]: `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\docs\providers\openrouter.md:8-22,24-50`
[^7]: `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\docs\providers\azure_ai.md:4-18,21-49,315-361`
[^8]: `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\docs\providers\gemini.md:5-36,38-48`
[^9]: `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\docs\providers\anthropic.md:21-28,170-227`
[^10]: `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\docs\providers\github_copilot.md:14-27,60-77,88-115`; `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\litellm_main.py:2611-2620`; `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\get_llm_provider_logic-current.py:782-789`
[^11]: `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\litellm_proxy_transformation.py:61-118`; `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\get_llm_provider_logic-current.py:122-127,715-721`
[^12]: `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\providers-index.json:83-91,243-251,1027-1035,1107-1115,1651-1659,1699-1707,1715-1723`
[^13]: `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\constants-current.py:736-744`; `C:\src\CodebrewRouter\research\sources\berriai-litellm\repo\get_llm_provider_logic-current.py:256-258,622-626`
