"""Deep Research Executor using Magentic One pattern for comprehensive market research."""

import asyncio
import json
import sys
from typing import Any, Mapping, Optional

from agent_framework import (
    ChatAgent,
    ChatClientProtocol,
    ChatMessage,
    Executor,
    MagenticBuilder,
    Role,
    WorkflowContext,
    handler,
    ai_function,
)
from typing import Annotated

from .schemas import MarketingStrategy
from .utils import extract_json_object


def create_research_agents(
    chat_client: ChatClientProtocol,
    search_tool: Any,
) -> dict[str, ChatAgent]:
    """Create specialized research agents for deep research workflow."""
    
    # Planner Agent: Analyzes topic and creates research plan
    planner = ChatAgent(
        chat_client=chat_client,
        name="research_planner",
        instructions="""You are a research planning expert. Analyze the marketing topic provided by the user and develop a comprehensive research plan.

**Task**: Create a deep research plan for the given topic, output in JSON format.

**Research plan should include**:
1. Topic breakdown: Decompose the topic into 3-5 researchable sub-dimensions
2. Search strategy: Design specific search queries for each dimension (one in English, one in the user's language)
3. Information needs: Clarify the types of information to collect for each dimension
4. Priority: Mark which dimensions are most important

**Output format**:
```json
{
  "topic_analysis": "Core understanding and marketing context of the topic",
  "research_dimensions": [
    {
      "dimension": "Dimension name",
      "priority": "high/medium/low",
      "search_queries": ["query 1", "query in English"],
      "info_needed": ["Types of information needed"]
    }
  ],
  "target_insights": ["Key insights expected to obtain"]
}
```

Output only JSON, do not include other content.
""",
    )
    
    # Researcher Agent: Executes searches and gathers information
    researcher = ChatAgent(
        chat_client=chat_client,
        name="researcher",
        instructions="""You are a market researcher. Execute searches and collect information based on the research plan.

**Task**: Use the web_search tool to execute search queries from the research plan and summarize findings.

**Workflow**:
1. Execute search queries in priority order
2. Use search_depth="advanced" for each search to get more comprehensive results
3. Extract key information: data, trends, case studies, pain points, opportunities
4. Record information sources

**Output format**:
```json
{
  "research_findings": [
    {
      "dimension": "Research dimension",
      "key_insights": ["Insight 1", "Insight 2"],
      "data_points": ["Specific data or statistics"],
      "trends": ["Discovered trends"],
      "sources": ["Source URLs"]
    }
  ],
  "market_overview": "Summary of overall market landscape",
  "competitive_landscape": "Description of competitive landscape",
  "opportunity_areas": ["Identified opportunity areas"]
}
```

⚠️ Important: You MUST actually call the web_search tool to get real data, do not fabricate information!
""",
        tools=[search_tool],
    )
    
    # Analyst Agent: Synthesizes research into marketing strategy
    analyst = ChatAgent(
        chat_client=chat_client,
        name="research_analyst",
        instructions="""You are a marketing strategy analyst. Synthesize research findings to generate a structured marketing strategy.

**CRITICAL: Language Detection & Consistency**
First, detect the language of the original user topic. Set `output_language` field accordingly:
- Chinese → "zh"
- English → "en"  
- Japanese → "ja"
- Korean → "ko"
- Other → use appropriate ISO 639-1 code

**ALL text content in your output MUST be written in the detected language.**

**Task**: Based on research data, generate MarketingStrategy JSON.

**Analysis framework**:
1. Target audience profile: Define a precise ICP based on research data
2. Pain point refinement: Extract real user pain points from research findings
3. Value proposition: Design differentiated selling points based on market opportunities
4. Content framework: Design content strategy based on trends
5. Keyword strategy: Select keywords based on search data

**Output format (must strictly follow)**:
```json
{
  "topic": "User topic",
  "output_language": "detected language code (zh/en/ja/ko/etc.)",
  "target_audience": "Detailed target audience description",
  "pain_points": ["Pain point 1", "Pain point 2", "Pain point 3"],
  "selling_points": ["Selling point 1", "Selling point 2", "Selling point 3"],
  "content_framework": ["Content framework step 1", "Step 2", "Step 3"],
  "tone_of_voice": "Tone style description",
  "brand_pillars": ["Brand pillar 1", "Brand pillar 2", "Brand pillar 3"],
  "keywords": ["Keyword 1", "Keyword 2", "Keyword 3"]
}
```

⚠️ Ensure each field has at least 3 entries!
⚠️ All text must be in the output_language!
Output only JSON, do not include Markdown code blocks.
""",
    )
    
    return {
        "planner": planner,
        "researcher": researcher,
        "analyst": analyst,
    }


class DeepResearchExecutor(Executor):
    """Executor that performs deep research using Magentic One pattern internally.
    
    This executor replaces the simple strategy_agent with a multi-agent research
    workflow that:
    1. Plans the research strategy
    2. Executes multiple searches across different dimensions
    3. Synthesizes findings into a comprehensive MarketingStrategy
    
    The output format matches what downstream agents expect from strategy_agent.
    """

    def __init__(
        self,
        chat_client: ChatClientProtocol,
        search_tool: Any,
        *,
        debug: bool = False,
        max_rounds: int = 10,
    ) -> None:
        super().__init__(id="deep-research-executor")
        self._chat_client = chat_client
        self._search_tool = search_tool
        self._debug = debug
        self._max_rounds = max_rounds
        self._author = "strategy_agent"  # Use same author name for compatibility
        
        # Create research agents
        self._research_agents = create_research_agents(chat_client, search_tool)

    def _debug_print(self, message: str) -> None:
        """Print debug message to stderr."""
        if self._debug:
            print(f"[DEBUG] {message}", file=sys.stderr)

    @handler
    async def handle(
        self,
        conversation: list[ChatMessage],
        ctx: WorkflowContext,
    ) -> None:
        """Execute deep research and output MarketingStrategy."""
        
        # Extract topic from conversation
        topic = self._extract_topic(conversation)
        self._debug_print(f"🔬 Deep Research Started for: {topic}")
        
        # Phase 1: Research Planning
        self._debug_print("📋 Phase 1: Research Planning")
        research_plan = await self._run_planning(topic)
        self._debug_print(f"   Plan created with {len(research_plan.get('research_dimensions', []))} dimensions")
        
        # Phase 2: Execute Research
        self._debug_print("🔍 Phase 2: Executing Research")
        research_findings = await self._run_research(topic, research_plan)
        self._debug_print(f"   Gathered findings from {len(research_findings.get('research_findings', []))} dimensions")
        
        # Phase 3: Synthesize Strategy
        self._debug_print("📊 Phase 3: Synthesizing Strategy")
        strategy_json = await self._run_analysis(topic, research_plan, research_findings)
        self._debug_print("   Strategy synthesized")
        
        # Validate the strategy
        try:
            strategy = MarketingStrategy.model_validate(strategy_json)
            output_text = strategy.model_dump_json(indent=2, ensure_ascii=False)
        except Exception as e:
            self._debug_print(f"⚠️ Strategy validation warning: {e}")
            # Fallback: output raw JSON
            output_text = json.dumps(strategy_json, ensure_ascii=False, indent=2)
        
        # Create output message with same author as strategy_agent
        strategy_message = ChatMessage(
            role=Role.ASSISTANT,
            author_name=self._author,
            text=output_text,
        )
        
        # Send updated conversation downstream
        updated_conversation = list(conversation)
        updated_conversation.append(strategy_message)
        await ctx.send_message(updated_conversation)
        
        self._debug_print("✅ Deep Research Completed")

    async def _run_planning(self, topic: str) -> dict[str, Any]:
        """Run the planner agent to create research plan."""
        planner = self._research_agents["planner"]
        
        prompt = f"Please create a deep research plan for the following marketing topic:\n\n{topic}"
        
        response = await planner.run(prompt)
        
        try:
            plan_text = response.text or ""
            plan_json = extract_json_object(plan_text)
            return json.loads(plan_json)
        except Exception as e:
            self._debug_print(f"⚠️ Planning parse error: {e}")
            # Return a basic plan
            return {
                "topic_analysis": topic,
                "research_dimensions": [
                    {"dimension": "Market Trends", "priority": "high", "search_queries": [f"{topic} market trends 2024 2025", f"{topic} industry outlook"], "info_needed": ["Market size", "Growth trends"]},
                    {"dimension": "Target Users", "priority": "high", "search_queries": [f"{topic} user persona pain points", f"{topic} target audience"], "info_needed": ["User characteristics", "Pain points and needs"]},
                    {"dimension": "Competitive Landscape", "priority": "medium", "search_queries": [f"{topic} competitor analysis", f"{topic} competitors"], "info_needed": ["Main competitors", "Differentiation opportunities"]},
                ],
                "target_insights": ["Market opportunities", "User pain points", "Differentiated positioning"]
            }

    async def _run_research(self, topic: str, plan: dict[str, Any]) -> dict[str, Any]:
        """Run the researcher agent to execute searches."""
        researcher = self._research_agents["researcher"]
        
        # Build research prompt with plan
        prompt = f"""Please execute searches and collect information based on the following research plan.

**Topic**: {topic}

**Research Plan**:
{json.dumps(plan, ensure_ascii=False, indent=2)}

Please use the web_search tool to execute search queries for each dimension and summarize findings.
Prioritize high priority dimensions first.
"""
        
        response = await researcher.run(prompt)
        
        try:
            findings_text = response.text or ""
            findings_json = extract_json_object(findings_text)
            return json.loads(findings_json)
        except Exception as e:
            self._debug_print(f"⚠️ Research parse error: {e}")
            # Return basic findings structure
            return {
                "research_findings": [],
                "market_overview": f"Research on {topic}",
                "competitive_landscape": "To be analyzed",
                "opportunity_areas": []
            }

    async def _run_analysis(
        self, 
        topic: str, 
        plan: dict[str, Any], 
        findings: dict[str, Any]
    ) -> dict[str, Any]:
        """Run the analyst agent to synthesize strategy."""
        analyst = self._research_agents["analyst"]
        
        prompt = f"""Please generate a structured marketing strategy based on the following research data.

**Topic**: {topic}

**Research Plan**:
{json.dumps(plan, ensure_ascii=False, indent=2)}

**Research Findings**:
{json.dumps(findings, ensure_ascii=False, indent=2)}

Please generate MarketingStrategy JSON, ensuring:
1. The topic field contains the original topic
2. Each list field (pain_points, selling_points, etc.) has at least 3 entries
3. Content is based on research findings, do not fabricate
"""
        
        response = await analyst.run(prompt)
        
        try:
            strategy_text = response.text or ""
            strategy_json = extract_json_object(strategy_text)
            result = json.loads(strategy_json)
            # Ensure topic is set
            if "topic" not in result:
                result["topic"] = topic
            # Ensure output_language is set (detect from topic if not present)
            if "output_language" not in result:
                result["output_language"] = self._detect_language(topic)
            return result
        except Exception as e:
            self._debug_print(f"⚠️ Analysis parse error: {e}")
            # Return minimal valid strategy
            return {
                "topic": topic,
                "output_language": self._detect_language(topic),
                "target_audience": "To be analyzed",
                "pain_points": ["Pain points to be researched"],
                "selling_points": ["Selling points to be defined"],
                "content_framework": ["Content framework to be designed"],
                "tone_of_voice": "Professional, trustworthy",
                "brand_pillars": ["Brand pillars to be determined"],
                "keywords": [topic]
            }

    @staticmethod
    def _detect_language(text: str) -> str:
        """Simple language detection based on character ranges."""
        # Check for CJK characters
        for char in text:
            if '\u4e00' <= char <= '\u9fff':
                return "zh"  # Chinese
            if '\u3040' <= char <= '\u309f' or '\u30a0' <= char <= '\u30ff':
                return "ja"  # Japanese
            if '\uac00' <= char <= '\ud7af':
                return "ko"  # Korean
        return "en"  # Default to English

    @staticmethod
    def _extract_topic(conversation: list[ChatMessage]) -> str:
        """Extract the user topic from conversation."""
        for message in conversation:
            if message.role == Role.USER and message.text:
                return message.text.strip()
        raise ValueError("User topic not found in conversation history.")
