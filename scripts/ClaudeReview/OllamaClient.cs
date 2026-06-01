using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeReview;

// OpenAI-compatible types used by Ollama's /v1/chat/completions endpoint
file record OllamaRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] List<OllamaMessage> Messages,
    [property: JsonPropertyName("stream")] bool Stream = false
);

file record OllamaMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content
);

file record OllamaResponse(
    [property: JsonPropertyName("choices")] List<OllamaChoice> Choices
);

file record OllamaChoice(
    [property: JsonPropertyName("message")] OllamaMessage Message
);

public class OllamaClient
{
    // Good code-review models available via: ollama pull <name>
    //   qwen2.5-coder:7b   — best for code tasks, fast on most machines
    //   llama3.2            — good general purpose, smaller download
    //   codellama           — Meta's code-focused model
    public const string DefaultModel = "qwen3.5:0.8b";

    private readonly string _model;
    private readonly string _baseUrl;
    private readonly HttpClient _http = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string SystemPrompt = """
        You are a senior code reviewer acting as a CI quality gate.
        Analyze the provided diff and return ONLY valid JSON matching this schema:
        {
          "verdict": "approve" | "request_changes" | "comment",
          "summary": "one-line summary",
          "issues": [
            {"severity": "critical|high|medium|low", "file": "path", "line": 42, "message": "..."}
          ],
          "quality_score": 0-100
        }

        Severity guide:
        - critical: security vulnerabilities, data loss, broken auth, injection risks
        - high: logic bugs, unchecked nulls, broken error handling, API contract violations
        - medium: dead code, poor naming, missing validation at boundaries
        - low: style, minor readability issues

        Return JSON only — no markdown fences, no explanation.
        """;

    public OllamaClient(string? model = null, string? baseUrl = null)
    {
        _model = model ?? DefaultModel;
        _baseUrl = (baseUrl ?? "http://localhost:11434") + "/v1/chat/completions";
    }

    public async Task<ReviewResult> ReviewDiffAsync(string diff)
    {
        var request = new OllamaRequest(
            Model: _model,
            Messages:
            [
                new OllamaMessage("system", SystemPrompt),
                new OllamaMessage("user", $"Review this diff:\n\n```diff\n{diff}\n```")
            ]
        );

        var body = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        Console.WriteLine($"Calling Ollama ({_model}) at {_baseUrl}...");
        var httpResponse = await _http.PostAsync(_baseUrl, content);
        var raw = await httpResponse.Content.ReadAsStringAsync();

        if (!httpResponse.IsSuccessStatusCode)
            throw new HttpRequestException($"Ollama error {(int)httpResponse.StatusCode}: {raw}");

        var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(raw)!;
        var text = ollamaResponse.Choices[0].Message.Content.Trim();

        // Some models wrap JSON in markdown fences despite being asked not to
        if (text.StartsWith("```"))
        {
            var lines = text.Split('\n');
            text = string.Join('\n', lines[1..^1]).Trim();
        }

        return JsonSerializer.Deserialize<ReviewResult>(text)
            ?? throw new InvalidOperationException($"Ollama returned unparseable JSON:\n{text}");
    }
}
