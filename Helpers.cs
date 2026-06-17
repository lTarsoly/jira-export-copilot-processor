using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

public static class Helpers
{
    public static string TicketToEmbedText(Dictionary<string, string> t) =>
    string.Join(" ", new[]
    {
        t.GetValueOrDefault("Issue key",  ""),
        t.GetValueOrDefault("Summary",    ""),
        t.GetValueOrDefault("Status",     ""),
        t.GetValueOrDefault("Issue Type", ""),
        t.GetValueOrDefault("Priority",   ""),
        t.GetValueOrDefault("Assignee",   ""),
        t.GetValueOrDefault("Labels",     ""),
    }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public static List<List<Dictionary<string, string>>> GroupTicketsByRelationships(List<Dictionary<string, string>> tickets)
    {
        var issueIdToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var issueKeyToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < tickets.Count; i++)
        {
            var ticket = tickets[i];
            var issueId = ticket.GetValueOrDefault("Issue id")?.Trim();
            var issueKey = ticket.GetValueOrDefault("Issue key")?.Trim();

            if (!string.IsNullOrWhiteSpace(issueId) && !issueIdToIndex.ContainsKey(issueId))
                issueIdToIndex[issueId] = i;

            if (!string.IsNullOrWhiteSpace(issueKey) && !issueKeyToIndex.ContainsKey(issueKey))
                issueKeyToIndex[issueKey] = i;
        }

        var adjacency = Enumerable.Range(0, tickets.Count)
            .Select(_ => new HashSet<int>())
            .ToList();

        static void Link(List<HashSet<int>> graph, int a, int b)
        {
            if (a == b) return;
            graph[a].Add(b);
            graph[b].Add(a);
        }

        for (int i = 0; i < tickets.Count; i++)
        {
            var ticket = tickets[i];

            var parentId = ticket.GetValueOrDefault("Parent id")?.Trim();
            if (!string.IsNullOrWhiteSpace(parentId) && issueIdToIndex.TryGetValue(parentId, out var parentIndex))
                Link(adjacency, i, parentIndex);

            var parentKey = ticket.GetValueOrDefault("Parent key")?.Trim();
            if (!string.IsNullOrWhiteSpace(parentKey) && issueKeyToIndex.TryGetValue(parentKey, out var parentKeyIndex))
                Link(adjacency, i, parentKeyIndex);

            foreach (var field in ticket)
            {
                var value = field.Value;
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (!IsReferenceLikeField(field.Key))
                    continue;

                foreach (var issueKey in ExtractIssueKeys(value))
                {
                    if (issueKeyToIndex.TryGetValue(issueKey, out var keyIndex))
                        Link(adjacency, i, keyIndex);
                }

                foreach (var issueId in ExtractIssueIds(value))
                {
                    if (issueIdToIndex.TryGetValue(issueId, out var idIndex))
                        Link(adjacency, i, idIndex);
                }
            }
        }

        var groups = new List<List<Dictionary<string, string>>>();
        var visited = new bool[tickets.Count];

        for (int i = 0; i < tickets.Count; i++)
        {
            if (visited[i])
                continue;

            var group = new List<Dictionary<string, string>>();
            var queue = new Queue<int>();
            queue.Enqueue(i);
            visited[i] = true;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                group.Add(tickets[current]);

                foreach (var neighbor in adjacency[current])
                {
                    if (visited[neighbor])
                        continue;

                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    public static string TicketGroupToEmbedText(List<Dictionary<string, string>> group)
    {
        const int maxChars = 8_000;
        var sb = new StringBuilder();

        foreach (var ticket in group)
        {
            sb.Append(TicketToEmbedText(ticket));
            sb.Append(' ');
            if (sb.Length >= maxChars)
                break;
        }

        return sb.ToString();
    }

    private static bool IsReferenceLikeField(string fieldName)
    {
        return fieldName.Contains("link", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("parent", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("epic", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("relat", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("cloner", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("block", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("depend", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("reference", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("issue", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ExtractIssueKeys(string text)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"\b[A-Z][A-Z0-9_]{1,15}-\d+\b");
        foreach (System.Text.RegularExpressions.Match match in matches)
            yield return match.Value;
    }

    private static IEnumerable<string> ExtractIssueIds(string text)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"\b\d{5,}\b");
        foreach (System.Text.RegularExpressions.Match match in matches)
            yield return match.Value;
    }

    public static float CosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        var sa = a.Span; var sb = b.Span;
        float dot = 0, na = 0, nb = 0;
        for (int i = 0; i < sa.Length; i++)
        {
            dot += sa[i] * sb[i];
            na += sa[i] * sa[i];
            nb += sb[i] * sb[i];
        }
        return dot / (MathF.Sqrt(na) * MathF.Sqrt(nb));
    }

    public static List<Dictionary<string, string>> LoadTickets(string csvFilePath)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            MissingFieldFound = null,
            BadDataFound = null,
            HeaderValidated = null,
        };

        var records = new List<Dictionary<string, string>>();
        using var reader = new StreamReader(csvFilePath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();
        var headers = csv.Context.Reader?.HeaderRecord ?? [];

        while (csv.Read())
        {
            var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var labelValues = new List<string>();

            for (int i = 0; i < headers.Length; i++)
            {
                var header = headers[i];
                var value = csv.GetField(i) ?? string.Empty;

                if (header.Equals("Labels", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        labelValues.Add(value);
                }
                else if (!record.ContainsKey(header))
                {
                    record[header] = value;
                }
            }

            if (labelValues.Count > 0)
                record["Labels"] = string.Join(", ", labelValues);

            records.Add(record);
        }

        return records;
    }

    public static string BuildTicketContext(List<Dictionary<string, string>> tickets)
    {
        const int maxChars = 18_000;    // ~4 500 tokens; leaves room for system prompt boilerplate + chat history within the 8 000-token request limit
        var sb = new StringBuilder();
        sb.AppendLine($"Total tickets: {tickets.Count}");
        sb.AppendLine();

        for (int i = 0; i < tickets.Count; i++)
        {
            var t = tickets[i];
            var entry = new StringBuilder();

            var key = t.GetValueOrDefault("Issue key", "N/A");
            var summary = t.GetValueOrDefault("Summary", "(no summary)");
            entry.AppendLine($"[{key}] {summary}");

            AppendField(entry, t, "Issue Type");
            AppendField(entry, t, "Status");
            AppendField(entry, t, "Priority");
            AppendField(entry, t, "Resolution");
            AppendField(entry, t, "Assignee");
            AppendField(entry, t, "Reporter");
            AppendField(entry, t, "Created");
            AppendField(entry, t, "Updated");
            AppendField(entry, t, "Labels");
            AppendField(entry, t, "Custom field (Story Points)", "Story Points");
            AppendField(entry, t, "Custom field (Sprint)", "Sprint");
            AppendField(entry, t, "Custom field (Epic Name)", "Epic");
            AppendField(entry, t, "Custom field (Epic Link)", "Epic Link");
            AppendFieldTruncated(entry, t, "Description", 600);
            AppendFieldTruncated(entry, t, "Custom field (Acceptance Criteria)", 400, "Acceptance Criteria");
            entry.AppendLine();

            if (sb.Length + entry.Length > maxChars)
            {
                sb.AppendLine($"[... {tickets.Count - i} more tickets omitted — context size limit reached ...]");
                break;
            }

            sb.Append(entry);
        }

        return sb.ToString();
    }

    public static void AppendField(StringBuilder sb, Dictionary<string, string> rec, string key, string? label = null)
    {
        if (rec.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            sb.AppendLine($"  {label ?? key}: {v}");
    }

    public static void AppendFieldTruncated(StringBuilder sb, Dictionary<string, string> rec, string key, int max, string? label = null)
    {
        if (rec.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
        {
            // Strip HTML tags so description tokens carry actual content, not markup
            v = System.Text.RegularExpressions.Regex.Replace(v, @"<[^>]+>", " ");
            v = System.Text.RegularExpressions.Regex.Replace(v, @"\s{2,}", " ").Trim();
            if (v.Length > max) v = v[..max] + "…";
            sb.AppendLine($"  {label ?? key}: {v}");
        }
    }

    public static void PrintError(string message) 
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
        Console.ResetColor();
    }

    public static void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}