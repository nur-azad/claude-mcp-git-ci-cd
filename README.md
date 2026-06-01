# Claude AI Code Review — CI/CD Quality Gate

Automated code review and quality gate for GitHub pull requests, powered by Claude AI. Built in C# (.NET 10) and designed as a **reusable platform workflow** — maintain it once, call it from any repo in your organisation.

---

## How It Works

```
Developer opens PR
        │
        ▼
  GitHub Actions
        │
   ┌────▼──────────────────────────────────┐
   │  1. Generate diff from PR changes      │
   │  2. Send diff to Claude / Ollama       │
   │  3. Parse structured review result     │
   │  4. Post comment on the PR             │
   │  5. Pass or block the merge            │
   └───────────────────────────────────────┘
        │
        ▼
  ✅ Merge allowed   or   ❌ Merge blocked
```

---

## Review Modes

| Mode | Command | Requires |
|---|---|---|
| **Dry run** | `DRY_RUN=true` | Nothing — mock response |
| **Ollama** | `USE_OLLAMA=true` | Ollama running locally |
| **Claude API** | _(default)_ | `ANTHROPIC_API_KEY` |
| **Agentic (Pattern B)** | `USE_TOOLS=true` | `ANTHROPIC_API_KEY` |

---

## Running Locally

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A `diff.txt` file (see below for how to generate one)

**Generate a diff to review:**

```powershell
git diff HEAD~1 > diff.txt
```

---

### Option 1 — Dry Run (no API key, mock response)

No external dependencies. Tests the full pipeline flow with a hardcoded mock response. Good for verifying the pipeline wiring works end-to-end.

```powershell
$env:DRY_RUN   = "true"
$env:DIFF_PATH = "diff.txt"

dotnet run --project scripts/ClaudeReview/ClaudeReview.csproj
```

**Expected output:**
```
DRY RUN MODE — Claude API will not be called.
Running Claude review (Pattern A — single turn)...
Verdict: comment | Score: 85/100
Summary: [DRY RUN] Mock review of 142-line diff. No real analysis performed.
  [LOW] example/File.cs:1 — [DRY RUN] This is a sample issue for pipeline testing.
Quality gate passed.
```

---

### Option 2 — Ollama (free, local AI, no API key)

Runs a real AI model on your machine. No API costs, no internet required after the model is downloaded.

**Step 1 — Install Ollama**

Download from [ollama.com](https://ollama.com) and install it.

**Step 2 — Pull a model**

```powershell
# Recommended — best for code review tasks (~4 GB)
ollama pull qwen2.5-coder:7b

# Lighter alternative (~2 GB)
ollama pull llama3.2
```

**Step 3 — Start Ollama**

```powershell
ollama serve
```

Ollama runs at `http://localhost:11434` by default.

**Step 4 — Run the review**

```powershell
$env:USE_OLLAMA = "true"
$env:DIFF_PATH  = "diff.txt"

dotnet run --project scripts/ClaudeReview/ClaudeReview.csproj
```

**Use a different model:**

```powershell
$env:USE_OLLAMA    = "true"
$env:OLLAMA_MODEL  = "llama3.2"
$env:DIFF_PATH     = "diff.txt"

dotnet run --project scripts/ClaudeReview/ClaudeReview.csproj
```

**Expected output:**
```
Running Ollama review (model: qwen2.5-coder:7b)...
Calling Ollama (qwen2.5-coder:7b) at http://localhost:11434/v1/chat/completions...
Verdict: request_changes | Score: 72/100
Summary: Missing null checks in new service methods
  [HIGH] Services/UserService.cs:48 — Return value not checked for null before use
  [LOW]  Services/UserService.cs:61 — Variable name 'x' is not descriptive
Quality gate passed.
```

---

### Option 3 — Claude API (Anthropic, best quality)

Uses Claude Sonnet — the highest quality review. Costs roughly **$0.01 per review**.

**Step 1 — Get an API key**

Sign up at [console.anthropic.com](https://console.anthropic.com) → API Keys → Create key.  
New accounts receive **$5 free credits** (enough for ~500 reviews).

> **Note:** This is separate from a Claude.ai Pro subscription. The API is a different product with usage-based billing.

**Step 2 — Run the review**

```powershell
$env:ANTHROPIC_API_KEY = "sk-ant-..."
$env:DIFF_PATH         = "diff.txt"

dotnet run --project scripts/ClaudeReview/ClaudeReview.csproj
```

**Pattern B — Agentic review (Claude reads full files for context):**

```powershell
$env:ANTHROPIC_API_KEY = "sk-ant-..."
$env:USE_TOOLS         = "true"
$env:DIFF_PATH         = "diff.txt"

dotnet run --project scripts/ClaudeReview/ClaudeReview.csproj
```

**Expected output:**
```
Running Claude review (Pattern A — single turn)...
Verdict: request_changes | Score: 74/100
Summary: HttpClient created per-request — potential socket exhaustion under load
  [HIGH]   ClaudeClient.cs:42  — HttpClient should be singleton or use IHttpClientFactory
  [MEDIUM] Program.cs:12       — Magic number 150_000 should be a named constant
  [LOW]    Models.cs:8         — Record names could be more descriptive
Quality gate passed.
```

---

### Post Review to a GitHub PR (any mode)

Set these extra variables to have the review comment posted directly on a pull request:

```powershell
$env:GH_TOKEN  = "github_pat_..."   # Personal Access Token with repo scope
$env:REPO      = "your-username/your-repo"
$env:PR_NUMBER = "42"
```

---

## Environment Variables Reference

| Variable | Required | Default | Description |
|---|---|---|---|
| `ANTHROPIC_API_KEY` | In CI mode | — | Anthropic API key |
| `DRY_RUN` | No | `false` | Skip API call, return mock response |
| `USE_OLLAMA` | No | `false` | Use local Ollama instead of Claude API |
| `OLLAMA_MODEL` | No | `qwen2.5-coder:7b` | Ollama model name |
| `OLLAMA_URL` | No | `http://localhost:11434` | Ollama base URL |
| `USE_TOOLS` | No | `false` | Enable Pattern B agentic review |
| `MIN_QUALITY_SCORE` | No | `70` | Score below this fails the quality gate |
| `DIFF_PATH` | No | `diff.txt` | Path to the diff file |
| `GH_TOKEN` | For PR comments | — | GitHub token for posting PR comments |
| `REPO` | For PR comments | — | Repository in `owner/repo` format |
| `PR_NUMBER` | For PR comments | — | Pull request number |

---

## GitHub Actions — CI/CD Setup

### This repo as a standalone pipeline

The workflow at [.github/workflows/claude-review.yml](.github/workflows/claude-review.yml) triggers automatically on every pull request.

**Add your API key once:**

Go to your repo → **Settings → Secrets → Actions → New secret**

```
Name:  ANTHROPIC_API_KEY
Value: sk-ant-...
```

The `GITHUB_TOKEN` for posting PR comments is provided automatically by GitHub — no setup needed.

---

### Using this as a platform workflow (reusable across repos)

This repo is designed as a **platform workflow** — other repos call it with a single `uses:` line and never copy any code.

In any consumer repo, create `.github/workflows/review.yml`:

```yaml
on:
  pull_request:
    types: [opened, synchronize]

jobs:
  code-review:
    uses: nur-azad/claude-mcp-git-ci-cd/.github/workflows/claude-review.yml@main
    with:
      min_quality_score: 80      # optional, default is 70
      use_tools: false           # optional, set true for Pattern B
    secrets:
      ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}
```

That's all. No C# code is copied into the consumer repo. When the platform workflow is updated, all consumer repos pick up the change automatically on their next run.

**Organisation-wide API key:** Set `ANTHROPIC_API_KEY` as an **organisation secret** in GitHub (Org Settings → Secrets → Actions) and it is automatically available to all repos — no per-repo setup needed.

---

## Quality Gate Behaviour

| Condition | Result |
|---|---|
| Any `critical` or `high` severity issue found | ❌ Merge blocked |
| Quality score below `MIN_QUALITY_SCORE` | ❌ Merge blocked |
| Only `medium` or `low` issues | ✅ Merge allowed (issues shown as comments) |
| No issues | ✅ Merge allowed |

**Severity guide:**

- `critical` — security vulnerabilities, data loss, broken auth, injection risks
- `high` — logic bugs, unchecked nulls, broken error handling, API contract violations
- `medium` — dead code, poor naming, missing boundary validation
- `low` — style, minor readability

---

## Project Structure

```
├── .github/workflows/
│   └── claude-review.yml      # GitHub Actions pipeline (reusable)
└── scripts/ClaudeReview/
    ├── ClaudeReview.csproj    # .NET 10, Octokit dependency
    ├── Models.cs              # Shared record types
    ├── ClaudeClient.cs        # Anthropic API client with prompt caching
    ├── OllamaClient.cs        # Ollama (OpenAI-compatible) client
    ├── GitHubReviewClient.cs  # Posts review comments via Octokit
    └── Program.cs             # Entry point and quality gate logic
```
