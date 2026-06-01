using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeReview;

public record ClaudeRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("system")] List<SystemBlock> System,
    [property: JsonPropertyName("messages")] List<Message> Messages,
    [property: JsonPropertyName("tools")] List<Tool>? Tools = null
);

public record SystemBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("cache_control")] CacheControl? CacheControl = null
);

public record CacheControl([property: JsonPropertyName("type")] string Type);

public record Message(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] object Content
);

public record ContentBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("input")] JsonElement? Input = null,
    [property: JsonPropertyName("tool_use_id")] string? ToolUseId = null,
    [property: JsonPropertyName("content")] string? Content = null
);

public record Tool(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("input_schema")] InputSchema InputSchema
);

public record InputSchema(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("properties")] Dictionary<string, SchemaProperty> Properties,
    [property: JsonPropertyName("required")] List<string> Required
);

public record SchemaProperty(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("description")] string? Description = null
);

public record ClaudeResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("content")] List<ContentBlock> Content,
    [property: JsonPropertyName("stop_reason")] string StopReason
);

public record ReviewResult(
    [property: JsonPropertyName("verdict")] string Verdict,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("issues")] List<ReviewIssue> Issues,
    [property: JsonPropertyName("quality_score")] int QualityScore
);

public record ReviewIssue(
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("message")] string Message
);

public record QualityGateResult(
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("score")] int Score
);
