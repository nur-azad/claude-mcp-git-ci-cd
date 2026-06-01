using ClaudeReview;

// DRY_RUN=true skips Claude API calls entirely — useful for testing the pipeline without an API key
var dryRun    = Environment.GetEnvironmentVariable("DRY_RUN") == "true";
var apiKey    = dryRun ? "dry-run" : Required("ANTHROPIC_API_KEY");
var ghToken   = Required("GH_TOKEN");
var repoEnv   = Required("REPO");  // format: "owner/repo"
var prNumber  = int.Parse(Required("PR_NUMBER"));

// Optional config with sensible defaults
var minScore  = int.TryParse(Environment.GetEnvironmentVariable("MIN_QUALITY_SCORE"), out var ms) ? ms : 70;
var useTools  = Environment.GetEnvironmentVariable("USE_TOOLS") == "true";
var diffPath  = Environment.GetEnvironmentVariable("DIFF_PATH") ?? "diff.txt";

var parts = repoEnv.Split('/', 2);
if (parts.Length != 2)
    throw new InvalidOperationException($"REPO must be in 'owner/repo' format, got: {repoEnv}");
var (owner, repoName) = (parts[0], parts[1]);

// Read diff produced by CI checkout step
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
    Console.WriteLine("No changes detected in diff. Skipping review.");
    return;
}

if (dryRun) Console.WriteLine("DRY RUN MODE — Claude API will not be called.");

var claude = new ClaudeClient(apiKey, dryRun);
var github = new GitHubReviewClient(ghToken, owner, repoName, prNumber);

if (!useTools)
{
    // Pattern A: single-turn review
    Console.WriteLine("Running Claude review (Pattern A — single turn)...");

    var result = await claude.ReviewDiffAsync(diff);
    await github.PostReviewCommentAsync(result);

    Console.WriteLine($"Verdict: {result.Verdict} | Score: {result.QualityScore}/100");
    Console.WriteLine($"Summary: {result.Summary}");

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
}
else
{
    // Pattern B: agentic loop with tool use (Claude can read full files)
    Console.WriteLine("Running Claude review (Pattern B — agentic with tool use)...");

    var gateResult = await claude.ReviewWithToolsAsync(diff);

    Console.WriteLine($"Score: {gateResult.Score}/100 | Passed: {gateResult.Passed}");
    Console.WriteLine($"Reason: {gateResult.Reason}");

    if (!gateResult.Passed)
    {
        Console.WriteLine($"QUALITY GATE FAILED: {gateResult.Reason}");
        Environment.Exit(1);
    }
}

Console.WriteLine("Quality gate passed.");

static string Required(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Required environment variable not set: {name}");

