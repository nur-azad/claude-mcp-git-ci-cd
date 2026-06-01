using Octokit;

namespace ClaudeReview;

public class GitHubReviewClient
{
    private readonly GitHubClient _github;
    private readonly string _owner;
    private readonly string _repo;
    private readonly int _prNumber;

    public GitHubReviewClient(string token, string owner, string repo, int prNumber)
    {
        _github = new GitHubClient(new ProductHeaderValue("claude-ci-review"))
        {
            Credentials = new Credentials(token)
        };
        _owner = owner;
        _repo = repo;
        _prNumber = prNumber;
    }

    public async Task PostReviewCommentAsync(ReviewResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Claude Code Review");
        sb.AppendLine();
        sb.AppendLine($"**Verdict:** `{result.Verdict}` | **Quality Score:** {result.QualityScore}/100");
        sb.AppendLine();
        sb.AppendLine($"> {result.Summary}");

        if (result.Issues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Issues Found");
            sb.AppendLine();

            foreach (var issue in result.Issues.OrderBy(i => SeverityRank(i.Severity)))
            {
                var emoji = issue.Severity switch
                {
                    "critical" => "🔴",
                    "high"     => "🟠",
                    "medium"   => "🟡",
                    _          => "🔵"
                };
                sb.AppendLine($"{emoji} **{issue.Severity.ToUpperInvariant()}** — `{issue.File}:{issue.Line}` — {issue.Message}");
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("No issues found. ✅");
        }

        await _github.Issue.Comment.Create(_owner, _repo, _prNumber, sb.ToString());
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "critical" => 0,
        "high"     => 1,
        "medium"   => 2,
        _          => 3
    };
}
