# JIRA Export Copilot Processor

A C# console application that lets you ask natural language questions about JIRA tickets exported to a CSV file, powered by GitHub Copilot (via GitHub Models).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A GitHub account with access to [GitHub Models](https://github.com/marketplace/models) (requires GitHub Copilot subscription or Models beta access)
- A GitHub Personal Access Token (PAT) with the `models:read` permission (or `repo` scope for classic tokens)
- The `COPILOT_GITHUB_TOKEN` environment variable set to your PAT

## Setup

### 1. Clone / open the project

```powershell
cd C:\your\path\JiraExportCopilotProcessor
```

### 2. Place your JIRA CSV export in the project root

Export your JIRA tickets via **Issues → Export → CSV (all fields)** and copy the resulting `.csv` file into the project root folder. The application automatically finds the first `.csv` file in the working directory at startup.

Example:
```
JiraExportCopilotProcessor\
  EPAM-JIRA-MSQ 2026-06-01T14_31_21+0200.csv   ← your export goes here
  Program.cs
  JiraExportCopilotProcessor.csproj
  ...
```

### 3. Create a GitHub Personal Access Token

1. Go to **GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens** (or classic tokens)
2. Create a new token with at least **Models: Read** permission
3. Copy the token value (starts with `ghp_`)

### 4. Set the `COPILOT_GITHUB_TOKEN` environment variable

**PowerShell (current session):**
```powershell
$env:COPILOT_GITHUB_TOKEN = "ghp_yourTokenHere"
```

**PowerShell (persistent, current user):**
```powershell
[System.Environment]::SetEnvironmentVariable("COPILOT_GITHUB_TOKEN", "ghp_yourTokenHere", "User")
```

**Command Prompt:**
```cmd
set COPILOT_GITHUB_TOKEN=ghp_yourTokenHere
```

## Running the Application

```powershell
dotnet run
```

Or run the published binary directly from the project directory — the application locates the CSV file relative to the current working directory.

## Optional: Choosing a Different Model

By default the application uses `gpt-4o`. You can switch to any model available on GitHub Models by setting the `COPILOT_MODEL` environment variable before running:

```powershell
$env:COPILOT_MODEL = "o4-mini"
dotnet run
```

Available models can be browsed at [github.com/marketplace/models](https://github.com/marketplace/models).

## Usage

Once running, type your question at the `You:` prompt and press Enter:

```
JIRA Ticket Analyzer — powered by GitHub Copilot
=================================================
Ask questions about your JIRA tickets.
Commands: 'help' | 'clear' | 'exit'

You: How many tickets are in New status?
You: Which tickets are assigned to John?
You: List all high-priority bugs.
You: Summarise the open tasks and their reporters.
You: Which ticket was updated most recently?
```

### Built-in Commands

| Command | Description |
|---------|-------------|
| `help`  | Show example questions and available commands |
| `clear` | Clear the conversation history (ticket data is kept) |
| `exit` / `quit` | Exit the application |

## Notes

- The ticket data is embedded into the AI system prompt at startup. Very large CSV exports (thousands of tickets) will be truncated at ~100 000 characters to stay within model context limits.
- Conversation history is maintained within a session so you can ask follow-up questions.
- The application streams AI responses token-by-token for a responsive feel.
