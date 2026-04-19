"""High level orchestration for the agentic marketing workflow."""

import asyncio
import sys
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import TYPE_CHECKING, Any, Callable, List, Mapping, Optional, Union

from agent_framework import (
    AgentRunUpdateEvent,
    ChatClientProtocol,
    ChatMessage,
    CheckpointStorage,
    FunctionCallContent,
    FunctionResultContent,
    InMemoryCheckpointStorage,
    Role,
    SequentialBuilder,
    Workflow,
    WorkflowOutputEvent,
    WorkflowContext,
    WorkflowStatusEvent,
    WorkflowRunState,
    Executor,
    handler,
)
from agent_framework._workflows._events import ExecutorInvokedEvent, ExecutorCompletedEvent

from .agents import MarketingAgents, create_marketing_agents
from .research import DeepResearchExecutor
from .schemas import CampaignPackage, CopywritingContent, ImageContent, MarketingStrategy, VideoScript
from .tools import FluxImageGenerationTools, ImageGenerationTools, PackagingTools, SoraVideoGenerationTools, TavilySearchTools
from .utils import extract_json_object, slugify, timestamp_id


@dataclass(slots=True)
class MarketingWorkflowConfig:
    """Runtime configuration knobs for the workflow."""

    persist_output: bool = True
    output_dir: str = "artifacts/campaigns"
    enable_image_generation: bool = False
    enable_video_generation: bool = False
    enable_deep_research: bool = False
    debug: bool = False
    checkpoint_storage: Optional[CheckpointStorage] = None
    default_agent_options: Optional[Mapping[str, Any]] = None
    per_agent_options: Optional[Mapping[str, Mapping[str, Any]]] = None


class AgenticMarketingWorkflow:
    """Public facade for running the orchestrated marketing workflow."""

    def __init__(
        self,
        chat_client: ChatClientProtocol,
        *,
        image_client: Optional[Any] = None,
        config: Optional[MarketingWorkflowConfig] = None,
    ) -> None:
        self._chat_client = chat_client
        self._config = config or MarketingWorkflowConfig()
        self._packaging_tools = PackagingTools(base_output_dir=Path(self._config.output_dir))
        
        # Initialize Tavily search tools for research
        self._tavily_tools = TavilySearchTools()
        
        # Initialize image tools based on configuration (output_dir set at runtime)
        self._flux_image_tools: Optional[FluxImageGenerationTools] = None
        self._image_tools: Optional[ImageGenerationTools] = None
        
        # Initialize video tools based on configuration
        self._sora_video_tools: Optional[SoraVideoGenerationTools] = None
        
        # Initialize deep research executor if enabled
        self._deep_research_executor: Optional[DeepResearchExecutor] = None
        
        if self._config.enable_image_generation:
            # Don't set output_dir yet - it will be set when run() is called
            self._flux_image_tools = FluxImageGenerationTools()
        elif image_client is not None:
            self._image_tools = ImageGenerationTools(image_client)
        
        if self._config.enable_video_generation:
            # Don't set output_dir yet - it will be set when run() is called
            self._sora_video_tools = SoraVideoGenerationTools()

        tool_registry: dict[str, list[Any]] = {}
        
        # Create deep research executor if enabled (will be used instead of strategy_agent)
        if self._config.enable_deep_research:
            self._deep_research_executor = DeepResearchExecutor(
                chat_client=chat_client,
                search_tool=self._tavily_tools.search,
                debug=self._config.debug,
            )
        
        # Register web search tool for strategy and copywriting agents
        tool_registry["strategy_agent"] = [self._tavily_tools.search]
        tool_registry["copywriting_agent"] = [self._tavily_tools.search]
        
        if self._flux_image_tools is not None:
            tool_registry["image_agent"] = [self._flux_image_tools.generate_image]
        elif self._image_tools is not None:
            tool_registry["image_agent"] = [self._image_tools.generate_image]
        
        # Register video generation tool for video agent
        if self._sora_video_tools is not None:
            tool_registry["video_agent"] = [self._sora_video_tools.generate_video]

        self._agents: MarketingAgents = create_marketing_agents(
            chat_client,
            tool_registry=tool_registry,
            default_agent_options=self._config.default_agent_options,
            per_agent_options=self._config.per_agent_options,
        )

        self._tool_registry = tool_registry
        self._packaging_executor: Optional[_PackagingExecutor] = None

    def _create_workflow(self, campaign_dir: str) -> Workflow:
        """Create a workflow with the given campaign directory."""
        packaging_executor = _PackagingExecutor(
            agent_names={
                "strategy": self._agents.strategy.name or "strategy_agent",
                "copywriting": self._agents.copywriting.name or "copywriting_agent",
                "image": self._agents.image.name or "image_agent",
                "video": self._agents.video.name or "video_agent",
            },
            packaging_tools=self._packaging_tools if self._config.persist_output else None,
            campaign_dir=campaign_dir,
        )
        self._packaging_executor = packaging_executor

        # Build participant list - use DeepResearchExecutor if enabled, else strategy_agent
        if self._deep_research_executor is not None:
            # Update executor debug setting for this run
            self._deep_research_executor._debug = self._config.debug
            strategy_participant = self._deep_research_executor
        else:
            strategy_participant = self._agents.strategy
        
        builder = SequentialBuilder().participants(
            [
                strategy_participant,
                self._agents.copywriting,
                self._agents.image,
                self._agents.video,
                packaging_executor,
            ]
        )

        checkpoint_storage = self._config.checkpoint_storage or InMemoryCheckpointStorage()
        return builder.with_checkpointing(checkpoint_storage).build()

    @property
    def workflow(self) -> Optional[Workflow]:
        return getattr(self, '_workflow', None)

    async def run(self, topic: str) -> CampaignPackage:
        """Execute the workflow end-to-end and return the packaged result."""

        # Generate campaign directory path with timestamp
        campaign_folder = f"{timestamp_id()}_campaign"
        campaign_dir = str(Path(self._config.output_dir) / campaign_folder)
        
        # Set image output directory to campaign's images subfolder
        if self._flux_image_tools is not None:
            self._flux_image_tools.set_output_dir(str(Path(campaign_dir) / "images"))
        
        # Set video output directory to campaign's video subfolder
        if self._sora_video_tools is not None:
            self._sora_video_tools.set_output_dir(str(Path(campaign_dir) / "video"))
        
        # Create workflow with the campaign directory
        workflow = self._create_workflow(campaign_dir)
        
        debug = self._config.debug
        if debug:
            self._debug_print(f"\n{'='*60}")
            self._debug_print(f"🚀 Marketing Workflow Started")
            self._debug_print(f"📁 Campaign Directory: {campaign_dir}")
            self._debug_print(f"📝 Topic: {topic}")
            self._debug_print(f"{'='*60}\n")

        final_package: Optional[CampaignPackage] = None
        current_executor: Optional[str] = None
        streaming_text: str = ""
        pending_tool_call: Optional[dict] = None  # Track tool call being streamed
        
        async for event in workflow.run_stream(topic):
            if debug:
                # Handle executor invocation events
                if isinstance(event, ExecutorInvokedEvent):
                    current_executor = event.executor_id
                    streaming_text = ""
                    pending_tool_call = None
                    self._debug_print(f"\n{'─'*50}")
                    self._debug_print(f"▶️  Executor Started: {current_executor}")
                    self._debug_print(f"   Time: {datetime.now().strftime('%H:%M:%S')}")
                    
                # Handle streaming text updates from agents
                elif isinstance(event, AgentRunUpdateEvent):
                    if event.data:
                        # Check for tool calls in contents
                        if hasattr(event.data, 'contents') and event.data.contents:
                            for content in event.data.contents:
                                if isinstance(content, FunctionCallContent):
                                    # Only print if we have a complete function name
                                    if content.name:
                                        # Print previous pending tool call if exists
                                        if pending_tool_call and pending_tool_call.get('name'):
                                            self._print_tool_call(pending_tool_call)
                                        
                                        # Start tracking new tool call
                                        pending_tool_call = {
                                            'name': content.name,
                                            'arguments': str(content.arguments) if content.arguments else ""
                                        }
                                    elif pending_tool_call and content.arguments:
                                        # Append arguments to pending call
                                        pending_tool_call['arguments'] += str(content.arguments)
                                        
                                elif isinstance(content, FunctionResultContent):
                                    # Print any pending tool call first
                                    if pending_tool_call and pending_tool_call.get('name'):
                                        self._print_tool_call(pending_tool_call)
                                        pending_tool_call = None
                                    
                                    # Print tool result
                                    result_dict = content.to_dict()
                                    result_str = str(result_dict.get('result', result_dict))
                                    if len(result_str) > 200:
                                        result_str = result_str[:200] + "..."
                                    self._debug_print(f"   ✅ Tool Result: {result_str}")
                        
                        # Handle streaming text
                        text_delta = event.data.text if hasattr(event.data, 'text') else ""
                        if text_delta:
                            streaming_text += text_delta
                            # Print streaming token to stderr for real-time feedback
                            print(text_delta, end="", flush=True, file=sys.stderr)
                
                # Handle executor completion events
                elif isinstance(event, ExecutorCompletedEvent):
                    # Print any pending tool call
                    if pending_tool_call and pending_tool_call.get('name'):
                        self._print_tool_call(pending_tool_call)
                        pending_tool_call = None
                    if streaming_text:
                        print(file=sys.stderr)  # New line after streaming
                    self._debug_print(f"✅ Executor Completed: {event.executor_id}")
                    self._debug_print(f"   Time: {datetime.now().strftime('%H:%M:%S')}")
                    
                    # Show output location for packaging executor
                    if event.executor_id == "packaging-executor" and campaign_dir:
                        self._debug_print(f"   📦 Output: {campaign_dir}")
                
                # Handle workflow status changes
                elif isinstance(event, WorkflowStatusEvent):
                    if event.state == WorkflowRunState.IDLE:
                        self._debug_print(f"\n{'='*60}")
                        self._debug_print(f"🏁 Workflow Completed")
                        self._debug_print(f"{'='*60}\n")
            
            # Capture final output
            if isinstance(event, WorkflowOutputEvent) and isinstance(event.data, CampaignPackage):
                final_package = event.data

        if final_package is None:
            raise RuntimeError("Workflow finished without emitting a CampaignPackage payload.")

        # package_path is already set by _PackagingExecutor
        return final_package
    
    def _debug_print(self, message: str) -> None:
        """Print debug message to stderr."""
        print(f"[DEBUG] {message}", file=sys.stderr)
    
    def _print_tool_call(self, tool_call: dict) -> None:
        """Print formatted tool call information."""
        name = tool_call.get('name', 'unknown')
        args = tool_call.get('arguments', '')
        
        self._debug_print(f"\n   🔧 Tool Call: {name}")
        if args:
            # Try to format JSON arguments nicely
            try:
                import json
                args_dict = json.loads(args) if isinstance(args, str) else args
                # Truncate long values
                for key, value in args_dict.items():
                    if isinstance(value, str) and len(value) > 100:
                        args_dict[key] = value[:100] + "..."
                args_str = json.dumps(args_dict, ensure_ascii=False, indent=2)
                for line in args_str.split('\n'):
                    self._debug_print(f"      {line}")
            except:
                args_str = str(args)
                if len(args_str) > 200:
                    args_str = args_str[:200] + "..."
                self._debug_print(f"      Arguments: {args_str}")

    def run_sync(self, topic: str) -> CampaignPackage:
        """Convenience synchronous wrapper around ``run``."""

        return asyncio.run(self.run(topic))


class _PackagingExecutor(Executor):
    """Final executor that assembles structured outputs into a CampaignPackage."""

    def __init__(
        self,
        *,
        agent_names: Mapping[str, str],
        packaging_tools: Optional[PackagingTools] = None,
        campaign_dir: Optional[str] = None,
    ) -> None:
        super().__init__(id="packaging-executor")
        self._agent_names = agent_names
        self._packaging_tools = packaging_tools
        self._campaign_dir = campaign_dir
        self._author = "packaging_executor"

    @handler
    async def handle(
        self,
        conversation: list[ChatMessage],
        ctx: WorkflowContext,
    ) -> None:
        package = self._build_package(conversation)
        if self._packaging_tools is not None and self._campaign_dir:
            package = package.with_package_path(
                self._packaging_tools.persist_package(package, campaign_dir=self._campaign_dir)
            )

        summary = ChatMessage(
            role=Role.ASSISTANT,
            author_name=self._author,
            text=package.model_dump_json(indent=2, ensure_ascii=False),
        )
        updated_conversation = list(conversation)
        updated_conversation.append(summary)
        await ctx.send_message(updated_conversation)
        await ctx.yield_output(package)

    def _build_package(self, conversation: list[ChatMessage]) -> CampaignPackage:
        topic = self._extract_topic(conversation)
        strategy = self._extract_model(conversation, self._agent_names["strategy"], MarketingStrategy)
        copywriting = self._extract_model(conversation, self._agent_names["copywriting"], CopywritingContent)
        images = self._extract_model(conversation, self._agent_names["image"], ImageContent, allow_empty=True)
        video = self._extract_model(conversation, self._agent_names["video"], VideoScript, allow_empty=True)

        campaign_id = slugify(f"{topic}-{strategy.target_audience}")
        return CampaignPackage(
            campaign_id=campaign_id,
            topic=topic,
            strategy=strategy,
            copywriting=copywriting,
            images=images,
            video=video,
        )

    @staticmethod
    def _extract_topic(conversation: list[ChatMessage]) -> str:
        for message in conversation:
            if message.role == Role.USER and message.text:
                return message.text.strip()
        raise ValueError("User topic not found in the conversation history.")

    def _extract_model(self, conversation: list[ChatMessage], author_name: str, model_cls: type[Any], allow_empty: bool = False) -> Any:
        """Extract and parse a model from agent output.
        
        Args:
            conversation: The conversation history
            author_name: Name of the agent whose output to extract
            model_cls: Pydantic model class to parse into
            allow_empty: If True, return empty model on failure instead of raising
            
        Returns:
            Parsed model instance
            
        Raises:
            ValueError: If parsing fails and allow_empty is False
        """
        try:
            raw_text = self._extract_message_text(conversation, author_name)
            payload = extract_json_object(raw_text)
            return model_cls.model_validate_json(payload)
        except Exception as e:
            # Catch ALL exceptions (ValueError, ValidationError, JSONDecodeError, etc.)
            if allow_empty:
                import sys
                print(f"[WARNING] Failed to parse {model_cls.__name__} from {author_name}: {e}", file=sys.stderr)
                print(f"[WARNING] Using empty {model_cls.__name__}", file=sys.stderr)
                return model_cls()
            # Re-raise with more context
            raise ValueError(f"Failed to parse {model_cls.__name__} from {author_name}: {e}") from e

    def _extract_message_text(self, conversation: list[ChatMessage], author_name: str) -> str:
        for message in reversed(conversation):
            if message.role == Role.ASSISTANT and (message.author_name or "") == author_name:
                # Try to get text content
                text = message.text
                if text:
                    return text
                
                # If text is empty, try to extract from contents
                if message.contents:
                    from agent_framework import TextContent
                    for content in message.contents:
                        if isinstance(content, TextContent) and content.text:
                            return content.text
                        # Also check for dict-style content
                        if isinstance(content, dict) and content.get("type") == "text":
                            text_val = content.get("text", "")
                            if text_val:
                                return text_val
                
                raise ValueError(f"Message from {author_name} is empty")
        raise ValueError(f"Missing assistant output from {author_name}")
