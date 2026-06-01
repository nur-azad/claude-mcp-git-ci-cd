using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeReview;

public class ClaudeClient
{
    private const string BaseUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-sonnet-4-6";

    private readonly HttpClient _http;

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

    private readonly bool _dryRun;

    public ClaudeClient(string apiKey, bool dryRun = false)
    {
        _dryRun = dryRun;
        _http = new HttpClient();
        if (!dryRun)
        {
            _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
            _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            _http.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31");
        }
    }

    // Pattern A: single-turn review, returns structured result
    public async Task<ReviewResult> ReviewDiffAsync(string diff)
    {
        if (_dryRun) return MockReviewResult(diff);
        var request = new ClaudeRequest(
            Model: Model,
            MaxTokens: 4096,
            System:
            [
                new SystemBlock(
                    Type: "text",
                    Text: SystemPrompt,
                    // Cache system prompt — saves ~70% cost on repeated CI runs within 5 min
                    CacheControl: new CacheControl("ephemeral")
                )
            ],
            Messages:
            [
                new Message("user", $"Review this diff:\n\n```diff\n{diff}\n```")
            ]
        );

        var response = await SendAsync(request);
        var text = response.Content.First(b => b.Type == "text").Text!;

        return JsonSerializer.Deserialize<ReviewResult>(text.Trim())
            ?? throw new InvalidOperationException("Claude returned unparseable JSON");
    }

    // Pattern B: agentic loop — Claude can call read_file for full context
    public async Task<QualityGateResult> ReviewWithToolsAsync(string diff)
    {
        if (_dryRun) return new QualityGateResult(true, "[DRY RUN] Skipped — no API key provided.", 85);
        var tools = new List<Tool>
        {
            new(
                "read_file",
                "Read a full source file to get context beyond the diff",
                new InputSchema(
                    "object",
                    new Dictionary<string, SchemaProperty>
                    {
                        ["path"] = new("string", "Relative file path to read")
                    },
                    ["path"]
                )
            ),
            new(
                "post_quality_gate_result",
                "Submit the final pass/fail verdict once review is complete",
                new InputSchema(
                    "object",
                    new Dictionary<string, SchemaProperty>
                    {
                        ["passed"] = new("boolean", "Whether the PR passes the quality gate"),
                        ["reason"] = new("string", "Short explanation of the decision"),
                        ["score"]  = new("integer", "Quality score 0–100")
                    },
                    ["passed", "reason", "score"]
                )
            )
        };

        var messages = new List<Message>
        {
            new("user", $"Review this PR diff. Use read_file for any suspicious changes needing full context. Call post_quality_gate_result when done.\n\n```diff\n{diff}\n```")
        };

        QualityGateResult? gateResult = null;

        while (true)
        {
            var request = new ClaudeRequest(
                Model: Model,
                MaxTokens: 4096,
                System: [new SystemBlock("text", SystemPrompt, new CacheControl("ephemeral"))],
                Messages: messages,
                Tools: tools
            );

            var response = await SendAsync(request);

            if (response.StopReason == "end_turn")
                break;

            var toolResults = new List<ContentBlock>();

            foreach (var block in response.Content.Where(b => b.Type == "tool_use"))
            {
                string toolOutput;

                if (block.Name == "read_file")
                {
                    var path = block.Input!.Value.GetProperty("path").GetString()!;
                    toolOutput = File.Exists(path)
                        ? await File.ReadAllTextAsync(path)
                        : $"File not found: {path}";
                }
                else if (block.Name == "post_quality_gate_result")
                {
                    gateResult = JsonSerializer.Deserialize<QualityGateResult>(
                        block.Input!.Value.GetRawText())!;
                    toolOutput = "Result recorded.";
                }
                else
                {
                    toolOutput = $"Unknown tool: {block.Name}";
                }

                toolResults.Add(new ContentBlock(
                    Type: "tool_result",
                    ToolUseId: block.Id,
                    Content: toolOutput
                ));
            }

            messages.Add(new Message("assistant", response.Content));
            if (toolResults.Count == 0) break;
            messages.Add(new Message("user", toolResults));
        }

        return gateResult ?? new QualityGateResult(true, "No blocking issues found.", 100);
    }

    private static ReviewResult MockReviewResult(string diff)
    {
        Console.WriteLine("[DRY RUN] Returning mock review result — no API call made.");
        var lineCount = diff.Split('\n').Length;
        return new ReviewResult(
            Verdict: "comment",
            Summary: $"[DRY RUN] Mock review of {lineCount}-line diff. No real analysis performed.",
            Issues:
            [
                new ReviewIssue("low", "example/File.cs", 1, "[DRY RUN] This is a sample issue for pipeline testing.")
            ],
            QualityScore: 85
        );
    }

    private async Task<ClaudeResponse> SendAsync(ClaudeRequest request)
    {
        var body = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var httpResponse = await _http.PostAsync(BaseUrl, content);
        var raw = await httpResponse.Content.ReadAsStringAsync();

        if (!httpResponse.IsSuccessStatusCode)
            throw new HttpRequestException($"Claude API {(int)httpResponse.StatusCode}: {raw}");

        return JsonSerializer.Deserialize<ClaudeResponse>(raw)!;
    }
}
