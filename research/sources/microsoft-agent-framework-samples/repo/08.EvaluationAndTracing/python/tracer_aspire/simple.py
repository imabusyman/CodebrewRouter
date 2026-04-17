import asyncio
import os
from random import randint
from typing import Annotated

import dotenv
from agent_framework import Agent
from agent_framework.foundry import FoundryChatClient
from agent_framework.observability import configure_otel_providers, get_tracer
from azure.identity.aio import AzureCliCredential
from opentelemetry.trace import SpanKind
from opentelemetry.trace.span import format_trace_id

from pydantic import Field

"""
This sample shows you can setup telemetry for an Azure AI Foundry agent.
It uses observability to send telemetry data to an OTLP endpoint (Aspire Dashboard).

You must configure the OTEL_EXPORTER_OTLP_ENDPOINT environment variable to point to your
telemetry collector (e.g., Aspire Dashboard at http://localhost:4317).
"""

# For loading the `FOUNDRY_PROJECT_ENDPOINT` and `OTEL_EXPORTER_OTLP_ENDPOINT` environment variables
dotenv.load_dotenv()


async def get_weather(
    location: Annotated[str, Field(description="The location to get the weather for.")],
) -> str:
    """Get the weather for a given location."""
    await asyncio.sleep(randint(0, 10) / 10.0)  # Simulate a network call
    conditions = ["sunny", "cloudy", "rainy", "stormy"]
    return f"The weather in {location} is {conditions[randint(0, 3)]} with a high of {randint(10, 30)}°C."


async def main():
    # This will enable tracing and configure the application to send telemetry data to the
    # OTLP endpoint (e.g., Aspire Dashboard) based on environment variables.
    # Reads OTEL_EXPORTER_OTLP_ENDPOINT and other standard OpenTelemetry env vars.
    configure_otel_providers()
    
    async with AzureCliCredential() as credential:
        # Create FoundryChatClient
        client = FoundryChatClient(credential=credential)

        questions = ["What's the weather in Amsterdam?", "and in Paris, and which is better?", "Why is the sky blue?"]

        with get_tracer().start_as_current_span("Single Agent Chat", kind=SpanKind.CLIENT) as current_span:
            print(f"Trace ID: {format_trace_id(current_span.get_span_context().trace_id)}")

            agent = Agent(
                client=client,
                tools=get_weather,
                name="WeatherAgent",
                instructions="You are a weather assistant.",
            )
            
            for question in questions:
                print(f"User: {question}")
                print(f"Agent: ", end="")
                response = await agent.run(question)
                print(response.text)


if __name__ == "__main__":
    asyncio.run(main())