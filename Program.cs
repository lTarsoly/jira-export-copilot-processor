using System.ClientModel;
using System.Text;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Embeddings;

Console.OutputEncoding = Encoding.UTF8;

// ── Locate CSV file ──────────────────────────────────────────────────────────
string? csvPath = null;
foreach (var dir in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
{
    var found = Directory.GetFiles(dir, "*.csv");
    if (found.Length > 0) { csvPath = found[0]; break; }
}

if (csvPath is null)
{
    Helpers.PrintError("Error: No CSV file found in the current directory.");
    return 1;
}

Console.WriteLine($"Loading JIRA tickets from: {Path.GetFileName(csvPath)}");
var tickets = Helpers.LoadTickets(csvPath);
Helpers.PrintSuccess($"Loaded {tickets.Count} ticket(s).");

var relatedTicketGroups = Helpers.GroupTicketsByRelationships(tickets);
Helpers.PrintSuccess($"Grouped into {relatedTicketGroups.Count} relationship cluster(s).");

// ── AI client via GitHub Models (OpenAI-compatible) ──────────────────────────
var token = Environment.GetEnvironmentVariable("COPILOT_GITHUB_TOKEN");
if (string.IsNullOrEmpty(token))
{
    Helpers.PrintError("""
        Error: COPILOT_GITHUB_TOKEN environment variable is not set.

        Set it with your GitHub personal access token (Copilot / Models access):
          PowerShell:  $env:COPILOT_GITHUB_TOKEN = "ghp_..."
          CMD:         set COPILOT_GITHUB_TOKEN=ghp_...
        """);
    return 1;
}

var modelName = Environment.GetEnvironmentVariable("COPILOT_MODEL") ?? "gpt-4o";
Console.WriteLine($"Using model : {modelName}");

var openAIClient = new OpenAIClient(
    new ApiKeyCredential(token),
    new OpenAIClientOptions { Endpoint = new Uri("https://models.inference.ai.azure.com") });

IChatClient chatClient = openAIClient
    .GetChatClient(modelName)
    .AsIChatClient();

EmbeddingClient embedder = openAIClient
    .GetEmbeddingClient("text-embedding-3-small");

// ── Build ticket embeddings (RAG index) ──────────────────────────────────────
Console.Write("Building ticket embeddings...");
var groupTexts = relatedTicketGroups.Select(Helpers.TicketGroupToEmbedText).ToList();
var embeddingResults = (await embedder.GenerateEmbeddingsAsync(groupTexts)).Value;
var groupedEmbeddings = relatedTicketGroups
    .Zip(embeddingResults, (group, e) => (group, vector: e.ToFloats()))
    .ToList();
Helpers.PrintSuccess($" done ({groupedEmbeddings.Count} vectors).");

// ── System prompt (ticket context injected per query via RAG) ─────────────────
var systemPrompt = """
    You are a helpful assistant specialised in analysing JIRA tickets.
    Answer the user's questions based on the relevant ticket data provided in each message.
    Be precise and concise. Reference ticket keys (e.g. PROJ-123) when relevant.
    If information is not available in the provided data, say so clearly.
    """;

var chatHistory = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };

// ── Chat loop ────────────────────────────────────────────────────────────────
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("JIRA Ticket Analyzer — powered by GitHub Copilot");
Console.WriteLine("=================================================");
Console.ResetColor();
Console.WriteLine("Ask questions about your JIRA tickets.");
Console.WriteLine("Commands: 'help' | 'clear' | 'exit'");
Console.WriteLine();

while (true)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write("You: ");
    Console.ResetColor();

    var input = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(input)) continue;

    switch (input.ToLowerInvariant())
    {
        case "exit":
        case "quit":
            Console.WriteLine("Goodbye!");
            return 0;

        case "clear":
            chatHistory.RemoveRange(1, chatHistory.Count - 1);
            Console.Clear();
            Console.WriteLine("Chat history cleared.");
            continue;

        case "help":
            Console.WriteLine("""

                Available commands:
                  help   - Show this help
                  clear  - Clear chat history (keeps ticket context)
                  exit   - Quit

                Example questions:
                  "How many tickets are in 'New' status?"
                  "Which tickets are assigned to John?"
                  "List all high-priority issues."
                  "Summarise open bugs."
                  "Which ticket has the most recent update?"
                """);
            continue;
    }

    // ── RAG: embed question, retrieve top relevant relationship groups ───────
    var queryVec = (await embedder.GenerateEmbeddingAsync(input)).Value.ToFloats();

    var topGroups = groupedEmbeddings
        .Select(te => (te.group, score: Helpers.CosineSimilarity(queryVec, te.vector)))
        .OrderByDescending(x => x.score)
        .Take(10)
        .Select(x => x.group)
        .ToList();

    var selectedTickets = new List<Dictionary<string, string>>();
    var seenIssueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var seenIssueKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var group in topGroups)
    {
        foreach (var ticket in group)
        {
            var issueId = ticket.GetValueOrDefault("Issue id") ?? string.Empty;
            var issueKey = ticket.GetValueOrDefault("Issue key") ?? string.Empty;

            var isNewId = string.IsNullOrWhiteSpace(issueId) || seenIssueIds.Add(issueId);
            var isNewKey = string.IsNullOrWhiteSpace(issueKey) || seenIssueKeys.Add(issueKey);

            if (isNewId && isNewKey)
                selectedTickets.Add(ticket);
        }
    }

    var context = Helpers.BuildTicketContext(selectedTickets);
    var userMessageWithContext = $"""
        Relevant JIRA tickets ({selectedTickets.Count} of {tickets.Count} total), selected by relationship-group relevance:
        ===
        {context}
        ===

        Question: {input}
        """;

    chatHistory.Add(new ChatMessage(ChatRole.User, userMessageWithContext));

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("\nCopilot: ");
    Console.ResetColor();

    var responseText = new StringBuilder();
    try
    {
        await foreach (var update in chatClient.GetStreamingResponseAsync(chatHistory))
        {
            var text = update.Text;
            if (text is not null)
            {
                Console.Write(text);
                responseText.Append(text);
            }
        }
        Console.WriteLine("\n");
        chatHistory.Add(new ChatMessage(ChatRole.Assistant, responseText.ToString()));
    }
    catch (Exception ex)
    {
        Helpers.PrintError($"\nError communicating with AI: {ex.Message}");
        // Remove the user message so history stays consistent
        if (chatHistory.Count > 1 && chatHistory[^1].Role == ChatRole.User)
            chatHistory.RemoveAt(chatHistory.Count - 1);
    }
}