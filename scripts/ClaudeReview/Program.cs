using ClaudeReview;

var dryRun   = Environment.GetEnvironmentVariable("DRY_RUN")    == "true";
var useOllama = Environment.GetEnvironmentVariable("USE_OLLAMA") == "true";
var useTools  = Environment.GetEnvironmentVariable("USE_TOOLS")  == "true";
var diffPath  = Environment.GetEnvironmentVariable("DIFF_PATH")  ?? "diff.txt";
var minScore  = int.TryParse(Environment.GetEnvironmentVariable("MIN_QUALITY_SCORE"), out var ms) ? ms : 70;

// GitHub posting is optional in local/Ollama mode
var ghToken  = Environment.GetEnvironmentVariable("GH_TOKEN");
var repoEnv  = Environment.GetEnvironmentVariable("REPO");
var prEnv    = Environment.GetEnvironmentVariable("PR_NUMBER");
var postToGitHub = !string.IsNullOrEmpty(ghToken)
               && !string.IsNullOrEmpty(repoEnv)
               && !string.IsNullOrEmpty(prEnv);

// In CI mode (not Ollama, not dry-run) these are required
if (!useOllama && !dryRun)
{
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
        throw new InvalidOperationException("ANTHROPIC_API_KEY is required. Set USE_OLLAMA=true to use Ollama instead.");
    if (!postToGitHub)
        throw new InvalidOperationException("GH_TOKEN, REPO, and PR_NUMBER are required in CI mode.");
}

// Read diff
if (!File.Exists(diffPath))
    throw new FileNotFoundException($"Diff file not found: {diffPath}");

var diff = await File.ReadAllTextAsync(diffPath);

if (diff.Length > 150_000)
{
    Console.WriteLine($"Diff is {diff.Length:N0} chars — truncating to 150,000");
    diff = diff[..150_000];
}

if (string.IsNullOrWhiteSpace(diff))
{
    Console.WriteLine("No changes in diff. Skipping review.");
    return;
}

// Run review
ReviewResult? result = null;

if (useOllama)
{
    var model   = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
    var baseUrl = Environment.GetEnvironmentVariable("OLLAMA_URL");
    var ollama  = new OllamaClient(model, baseUrl);

    Console.WriteLine($"Running Ollama review (model: {model ?? OllamaClient.DefaultModel})...");
    result = await ollama.ReviewDiffAsync(diff);
}
else
{
    var apiKey = dryRun ? "dry-run" : Required("ANTHROPIC_API_KEY");
    var claude = new ClaudeClient(apiKey, dryRun);

    if (dryRun)
        Console.WriteLine("DRY RUN MODE — Claude API will not be called.");

    if (!useTools)
    {
        Console.WriteLine("Running Claude review (Pattern A — single turn)...");
        result = await claude.ReviewDiffAsync(diff);
    }
    else
    {
        Console.WriteLine("Running Claude review (Pattern B — agentic with tool use)...");
        var gateResult = await claude.ReviewWithToolsAsync(diff);

        Console.WriteLine($"Score: {gateResult.Score}/100 | Passed: {gateResult.Passed}");
        Console.WriteLine($"Reason: {gateResult.Reason}");

        if (!gateResult.Passed)
        {
            Console.WriteLine($"QUALITY GATE FAILED: {gateResult.Reason}");
            Environment.Exit(1);
        }

        Console.WriteLine("Quality gate passed.");
        return;
    }
}

// Print result to console (always)
Console.WriteLine($"Verdict: {result.Verdict} | Score: {result.QualityScore}/100");
Console.WriteLine($"Summary: {result.Summary}");
foreach (var issue in result.Issues)
    Console.WriteLine($"  [{issue.Severity.ToUpperInvariant()}] {issue.File}:{issue.Line} — {issue.Message}");

// Post to GitHub PR (only in CI or when tokens are available)
if (postToGitHub)
{
    var parts = repoEnv!.Split('/', 2);
    if (parts.Length != 2)
        throw new InvalidOperationException($"REPO must be 'owner/repo', got: {repoEnv}");

    var github = new GitHubReviewClient(ghToken!, parts[0], parts[1], int.Parse(prEnv!));
    await github.PostReviewCommentAsync(result);
    Console.WriteLine("Review comment posted to GitHub PR.");
}

// Quality gate
var blocking = result.Issues.Count(i => i.Severity is "critical" or "high");
if (blocking > 0)
{
    Console.WriteLine($"QUALITY GATE FAILED: {blocking} critical/high issue(s) found.");
    Environment.Exit(1);
}
if (result.QualityScore < minScore)
{
    Console.WriteLine($"QUALITY GATE FAILED: score {result.QualityScore} is below threshold {minScore}.");
    Environment.Exit(1);
}

Console.WriteLine("Quality gate passed.");

static string Required(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Required environment variable not set: {name}");
