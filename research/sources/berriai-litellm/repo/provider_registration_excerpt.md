# Integrate as a Model Provider

Source URL: `https://docs.litellm.ai/docs/provider_registration/`

## Quick Start for OpenAI-Compatible Providers

If your API is OpenAI-compatible, you can add support by editing a single JSON file. See `Adding OpenAI-Compatible Providers`.

This guide focuses on how to set up the classes and configuration necessary to act as a chat provider.

## Overview

LiteLLM acts as a wrapper: it takes OpenAI-style requests and routes them to a provider API, then adapts the provider output into a standard output.

To integrate as a provider, you write a module that acts as an adapter between the LiteLLM API and the provider API.

The module includes methods that:

- Validate the request
- Transform the requests sent to the provider API
- Transform responses from the provider API into responses returned by LiteLLM

## 1. Create Your Config Class

Create a new directory:

`litellm/llms/your_provider_name_here`

Add a chat transformation file:

`litellm/llms/your_provider_name_here/chat/transformation.py`

Define a config class extending `BaseConfig`.

## 2. Add Yourself To Various Places In The Code Base

### `litellm/__init__.py`

- Add your key to the list of keys.
- Import your config.

### `litellm/main.py`

- Import your provider config or handler.
- Add request routing so the provider name maps to your implementation.

### `litellm/constants.py`

- Add your provider to `LITELLM_CHAT_PROVIDERS`.

### `litellm/litellm_core_utils/get_llm_provider_logic.py`

- Add provider-prefix detection so model names route to your provider.

### `litellm/litellm_core_utils/streaming_handler.py`

- Update streaming logic if the provider uses a custom chunk format.

## 3. Write a Test File to Iterate Your Code

Add tests under:

`tests/test_litellm/llms/my_provider/chat/test.py`

Test with `completion(model="my_provider/your-model", messages=[...], api_key="...")`.

## 4. Implement Required Methods

The guide points implementers to `completion()` in `litellm/llms/custom_httpx/llm_http_handler.py` and calls out these required methods:

- `validate_environment`
- `get_complete_url`
- `transform_request`
- `transform_response`
- `get_sync_custom_stream_wrapper`
- `get_async_custom_stream_wrapper`

## Tests

Create tests in `tests/test_litellm/llms/my_provider/chat/test.py`.
