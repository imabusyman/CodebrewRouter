using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blaze.LlmGateway.Api;

/// <summary>
/// Custom JSON converter that allows ChatMessageDto.Content to be either a string (simple text)
/// or an array of content parts (text, images, audio) for multimodal support.
/// </summary>
public class ChatMessageContentConverter : JsonConverter<ChatMessageDto>
{
    public override ChatMessageDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDoc = JsonDocument.ParseValue(ref reader);
        var root = jsonDoc.RootElement;

        if (!root.TryGetProperty("role", out var roleElement))
            throw new JsonException("Missing required 'role' field");

        var role = roleElement.GetString() ?? throw new JsonException("Role must be a string");

        // Content can be either a string or an array of content parts
        string content = "";
        if (root.TryGetProperty("content", out var contentElement))
        {
            if (contentElement.ValueKind == JsonValueKind.String)
            {
                // Simple text content
                content = contentElement.GetString() ?? "";
            }
            else if (contentElement.ValueKind == JsonValueKind.Array)
            {
                // For now, extract text parts only; full multimodal handling in AIContent conversion
                var textParts = new List<string>();
                foreach (var part in contentElement.EnumerateArray())
                {
                    if (part.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "text")
                    {
                        if (part.TryGetProperty("text", out var textEl))
                            textParts.Add(textEl.GetString() ?? "");
                    }
                }
                content = string.Join("\n", textParts);
            }
        }

        return new ChatMessageDto(role, content);
    }

    public override void Write(Utf8JsonWriter writer, ChatMessageDto value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("role", value.Role);
        writer.WriteString("content", value.Content);
        writer.WriteEndObject();
    }
}
