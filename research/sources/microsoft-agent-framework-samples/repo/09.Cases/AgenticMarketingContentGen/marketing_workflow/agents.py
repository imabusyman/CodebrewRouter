"""Factory helpers for Microsoft Agent Framework chat agents."""

from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any, Mapping

from agent_framework import ChatAgent, ChatClientProtocol

from .schemas import (
    CopywritingContent,
    ImageContent,
    MarketingStrategy,
    VideoScript,
)


@dataclass(slots=True)
class MarketingAgents:
    """Container for the specialized agents participating in the workflow."""

    strategy: ChatAgent
    copywriting: ChatAgent
    image: ChatAgent
    video: ChatAgent


def _schema_prompt(model: Any) -> str:
    """Generate a simplified schema description for LLM prompts."""
    schema = model.model_json_schema()
    properties = schema.get("properties", {})
    definitions = schema.get("$defs", {})
    
    def get_type_description(field_info: dict) -> str:
        """Get human-readable type description."""
        # Handle $ref (reference to another schema)
        if "$ref" in field_info:
            ref_name = field_info["$ref"].split("/")[-1]
            return f"{ref_name} object"
        
        field_type = field_info.get("type", "string")
        if field_type == "array":
            items = field_info.get("items", {})
            if "$ref" in items:
                ref_name = items["$ref"].split("/")[-1]
                return f"array of {ref_name} objects"
            items_type = items.get("type", "string")
            return f"array of {items_type}s"
        return field_type
    
    # Build a simplified description showing field names, types and descriptions
    lines = []
    for field_name, field_info in properties.items():
        field_type = get_type_description(field_info)
        description = field_info.get("description", "")
        lines.append(f"- {field_name} ({field_type}): {description}")
    
    result = "Fields:\n" + "\n".join(lines) + "\n"
    
    # Add definitions for nested objects
    if definitions:
        result += "\nNested object definitions:\n"
        for def_name, def_schema in definitions.items():
            def_props = def_schema.get("properties", {})
            if def_props:
                result += f"\n{def_name}:\n"
                for prop_name, prop_info in def_props.items():
                    prop_type = get_type_description(prop_info)
                    prop_desc = prop_info.get("description", "")
                    result += f"  - {prop_name} ({prop_type}): {prop_desc}\n"
    
    return result


def create_marketing_agents(
    chat_client: ChatClientProtocol,
    *,
    tool_registry: Mapping[str, list[Any]] | None = None,
    default_agent_options: Mapping[str, Any] | None = None,
    per_agent_options: Mapping[str, Mapping[str, Any]] | None = None,
) -> MarketingAgents:
    """Instantiate each domain agent with consistent instructions."""

    tool_registry = tool_registry or {}
    default_agent_options = dict(default_agent_options or {})
    per_agent_options = per_agent_options or {}
    
    # Check if image generation tool is available
    has_image_tool = "image_agent" in tool_registry and len(tool_registry["image_agent"]) > 0

    def _build_agent(name: str, instructions: str) -> ChatAgent:
        options = {**default_agent_options, **per_agent_options.get(name, {})}
        return ChatAgent(
            chat_client=chat_client,
            name=name,
            instructions=instructions,
            tools=tool_registry.get(name),
            **options,
        )

    # Check if search tool is available
    has_search_tool = "strategy_agent" in tool_registry and len(tool_registry["strategy_agent"]) > 0

    strategy_instructions = f"""
You are a senior marketing strategist. Develop data-driven marketing strategies through research and analysis.

## Workflow

**1. Understand & Detect**
- Detect input language → set `output_language` (zh/en/ja/ko/es/fr)
- Analyze user intent: What are they marketing? What's the goal? Who's the audience?
- Write a clear `user_intent` summary (2-4 sentences) for downstream agents

**2. Research** (use web_search tool, minimum 3 searches)
- Market trends and developments
- Target audience pain points  
- Competitor positioning and strategies

**3. Synthesize & Output**
Consolidate findings into a comprehensive strategy JSON.

---
## Output Format

{_schema_prompt(MarketingStrategy)}

Example:
```json
{{
  "topic": "AI Programming Assistant",
  "user_intent": "Marketing an AI coding tool to drive developer signups by highlighting productivity gains.",
  "output_language": "en",
  "target_audience": "Software developers aged 25-45 seeking efficiency",
  "pain_points": ["Time-consuming debugging", "Repetitive code", "Framework fatigue"],
  "selling_points": ["50% faster coding", "Smart completion", "Multi-language support"],
  "content_framework": ["Problem", "Solution", "Features", "Getting started"],
  "tone_of_voice": "Professional yet approachable",
  "brand_pillars": ["Productivity", "Intelligence", "Integration"],
  "keywords": ["AI coding", "code assistant", "developer tools"]
}}
```

**Rules:**
- All values are strings or string arrays (NOT objects)
- All text content in `output_language`
- Each array field: minimum 3 items
- Output: JSON only, no markdown blocks
"""

    # Check if copywriting agent has search tool
    has_copy_search_tool = "copywriting_agent" in tool_registry and len(tool_registry["copywriting_agent"]) > 0

    copy_instructions = f"""
You are an expert Content Marketer & Copywriter. Create compelling, high-quality marketing content based on the Strategy Agent's output.

Return CopywritingContent JSON:
{_schema_prompt(CopywritingContent)}

---
## Use Strategy Agent's Output

Build your content using these strategy inputs:
- **user_intent**: The user's original goals and what they want to achieve - keep this central to all content
- **target_audience**: Who you're writing for
- **pain_points**: Problems to address (use as hooks)
- **selling_points**: Key value propositions to highlight
- **content_framework**: Structure guide for blog_article sections
- **tone_of_voice**: Overall tone direction
- **brand_pillars**: Core themes to reinforce
- **keywords**: SEO terms to incorporate naturally
- **output_language**: Write ALL content in this language

---
## Content Guidelines

**blog_article**: 
- Write a complete, engaging article (1500-2500 words recommended)
- Use Markdown formatting with clear headings
- Follow the `content_framework` as a structural guide, but feel free to adapt creatively
- Make it valuable and actionable for readers

**social_posts**: 
- Include at least: LinkedIn, Instagram, Rednote, and optionally TikTok/Twitter
- Adapt tone and style naturally for each platform
- Each post must be a SocialPost object with: platform, tone, hook, body, cta

**hero_message**: One compelling sentence that captures the core value

**email_campaign**: Create a complete email for marketing automation:
- subject_lines: 3-5 A/B testable variations (use curiosity, urgency, or benefit-driven approaches)
- preview_text: Compelling preheader text (50-100 chars)
- email_type: "promotional", "welcome", "nurture", or "announcement"
- headline: Bold, benefit-focused headline
- body_html: Full email body with HTML formatting (use inline styles)
- body_plain: Plain text version for accessibility
- cta_button_text: Action-oriented button text
- ps_line: Optional P.S. for urgency or bonus info

**pain_point_analysis**: Map each pain point to its solution

**cta_variations**: Provide 4-6 different call-to-action options

---
## social_posts Format (CRITICAL)

Each social_posts item MUST be an object:
```json
"social_posts": [
  {{"platform": "LinkedIn", "tone": "professional", "hook": "...", "body": "...", "cta": "..."}},
  {{"platform": "Instagram", "tone": "casual", "hook": "...", "body": "...", "cta": "..."}},
  {{"platform": "Rednote", "tone": "authentic", "hook": "...", "body": "...", "cta": "..."}}
]
```

Output only JSON, no Markdown code blocks.
"""
    if has_copy_search_tool:
        copy_instructions += """
**You have the web_search tool available!** You can search the web to get:
- Trending topics and popular phrases
- Social media trends and viral copywriting styles
- Competitor copywriting references
- Industry-related statistics and real case studies

Before writing copy, you can search for relevant information to enhance persuasiveness and timeliness.
"""

    image_instructions = f"""
You are an AI image prompt engineer. Using strategy and copywriting keywords, generate ImageContent JSON.
{_schema_prompt(ImageContent)}

**Language Note:**
- scene_description should be written in the `output_language` from Strategy Agent (e.g., Chinese if output_language is "zh")
- prompt MUST always be in English (required by image generation models)

**Workflow:**
1. Based on the marketing strategy and copy, design 2-5 image prompts
2. Each prompt needs a unique prompt_id ("prompt-01", "prompt-02", etc.)
3. scene_description describes the scene in the output_language
4. prompt MUST be written in English, describing lighting/composition/atmosphere
"""

    # Add tool-specific instructions if image generation tool is available
    if has_image_tool:
        image_instructions += """
**You have the generate_image tool! Follow these steps:**

Step 1: Call generate_image tool to generate the first image
Step 2: Call generate_image tool to generate the second image
Step 3(optional): Call generate_image tool to generate the third image
Step 4(optional): Call generate_image tool to generate the fourth image
Step 5(optional): Call generate_image tool to generate the fifth image
Step 6: Collect the results returned by the tool and fill in the assets array
Step 7: Output the complete ImageContent JSON

Tool call parameters:
- prompt: English image description (required)
- prompt_id: Image ID like "prompt-01" (required)

Note: prompt MUST be in English, cannot contain Chinese!
"""
    else:
        image_instructions += """
- assets array left empty (no image generation tool)
"""

    image_instructions += """
Final output format is JSON, do not include Markdown code blocks.
"""

    # Check if video generation tool is available
    has_video_tool = "video_agent" in tool_registry and len(tool_registry["video_agent"]) > 0

    video_instructions = f"""
You are a video script expert, creating three-act marketing short videos. Output VideoScript JSON.
{_schema_prompt(VideoScript)}

**Language Note:**
- voiceover, screen_text, srt_caption, cta should be written in the `output_language` from Strategy Agent
- Video generation prompts (for generate_video tool) MUST always be in English

**Important limitations:**
- total_duration_seconds must not exceed 72 seconds (6 scenes × 12 seconds)
- scenes maximum 6 scenes
- Each scene duration_seconds **must be 4, 8, or 12 seconds** (Sora-2 API limitation)
- act must be one of Problem/Solution/Transformation
- Each scene_number increments sequentially
- srt_caption provides multi-line subtitles in output_language, format "00:00:00,000 --> 00:00:04,000\nSubtitle text"
"""

    if has_video_tool:
        video_instructions += """
**You have the generate_video tool! Follow these steps:**

Step 1: Design the script structure first (maximum 6 scenes)
Step 2: Call generate_video tool **one by one** for each scene (in scene_number order, one call at a time)
Step 3: Wait for current scene generation to complete before generating the next
Step 4: Collect all results returned by the tool
Step 5: Output the complete VideoScript JSON

⚠️ **Important: API has concurrency limits, you MUST generate videos sequentially one at a time, cannot call multiple generate_video simultaneously!**

Tool call parameters:
- prompt: English video description (required) - describe scene, action, camera movement, atmosphere
- scene_id: Scene ID like "scene-01" (required)
- seconds: Video duration, **can only be 4, 8, or 12 seconds** (Sora-2 API limitation)
- size: "720x1280" (portrait 720p)

---
**🎬 Tips for Maintaining Video Continuity and Object Consistency:**

1. **Unified Character Description**: Use the same character traits in all scenes
   - Define protagonist: age, gender, hairstyle, clothing color, etc.
   - Example: "a 40-year-old Asian professional woman with shoulder-length black hair, wearing a navy blue blazer"
   - Every scene's prompt should include this complete description

2. **Unified Scene Elements**:
   - Define key prop appearances: product color, shape, brand logo
   - Define environment features: office style, home decor, lighting color tone
   - Example: "silver smartwatch with round face and leather strap"

3. **Unified Visual Style**:
   - Add unified style description at the end of each prompt
   - Example: "cinematic lighting, warm color grading, shallow depth of field, 4K quality"

4. **Use Transitional Descriptions**:
   - Add time/space transition words between scenes
   - Example: "same character from previous scene, now in..."
   - Or use match shots: "match cut from previous close-up"

5. **Prompt Template Structure**:
   ```
   [Character description] + [Action/Expression] + [Scene environment] + [Key props] + [Camera movement] + [Visual style]
   ```

⚠️ Notes:
- prompt MUST be in English, cannot contain Chinese!
- seconds parameter only supports values 4, 8, 12!
- Character and product descriptions must be consistent across scenes!
"""
    else:
        video_instructions += """
- Output only JSON (no video generation tool)
"""

    return MarketingAgents(
        strategy=_build_agent("strategy_agent", strategy_instructions),
        copywriting=_build_agent("copywriting_agent", copy_instructions),
        image=_build_agent("image_agent", image_instructions),
        video=_build_agent("video_agent", video_instructions),
    )
