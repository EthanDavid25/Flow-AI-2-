using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlowAI.Core.Parsing;

/// <summary>
/// Parses structured action JSON blocks from AI responses.
/// Supports:
/// - Fenced blocks (```action, ```json, or untagged ```)
/// - Inline tags ([ACTION:{...}])
/// - Raw JSON objects ({"action": ...})
/// - JSON arrays ([{"action": ...}, ...])
/// - Embedded JSON within conversational text
///
/// Strips action blocks so raw JSON is never shown in chat or spoken aloud.
/// </summary>
public static class ActionJsonParser
{
    // Matches ```action ... ```, ```json ... ```, or plain ``` ... ``` containing "action"
    private static readonly Regex FencedBlockRegex = new(
        @"```(?:action|json)?\s*\r?\n([\s\S]*?)\r?\n?\s*```",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches [ACTION:{...}] inline tags
    private static readonly Regex InlineActionRegex = new(
        @"\[ACTION:([\s\S]*?)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches raw JSON objects containing an "action" key
    private static readonly Regex EmbeddedJsonRegex = new(
        @"\{[^{}]*""action""\s*:[^{}]*\}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Extracts all action blocks from an AI response text.</summary>
    public static List<ParsedAction> ParseActions(string text)
    {
        var actions = new List<ParsedAction>();
        if (string.IsNullOrWhiteSpace(text)) return actions;

        string trimmed = text.Trim();

        // 1. Try parsing whole trimmed text as JSON array first: [{"action": ...}]
        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
        {
            var arrayActions = TryParseJsonArray(trimmed);
            if (arrayActions.Count > 0) return arrayActions;
        }

        // 2. Try parsing whole trimmed text as single JSON object: {"action": ...}
        if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
        {
            var single = TryParseJson(trimmed);
            if (single != null)
            {
                actions.Add(single);
                return actions;
            }
        }

        // 3. Parse fenced blocks
        foreach (Match m in FencedBlockRegex.Matches(text))
        {
            string content = m.Groups[1].Value.Trim();
            if (content.StartsWith("["))
            {
                actions.AddRange(TryParseJsonArray(content));
            }
            else
            {
                var action = TryParseJson(content);
                if (action != null) actions.Add(action);
            }
        }

        if (actions.Count > 0) return actions;

        // 4. Parse inline tags [ACTION:...]
        foreach (Match m in InlineActionRegex.Matches(text))
        {
            var action = TryParseJson(m.Groups[1].Value.Trim());
            if (action != null) actions.Add(action);
        }

        if (actions.Count > 0) return actions;

        // 5. Look for embedded JSON objects { ... "action": ... }
        foreach (Match m in EmbeddedJsonRegex.Matches(text))
        {
            var action = TryParseJson(m.Value.Trim());
            if (action != null) actions.Add(action);
        }

        return actions;
    }

    /// <summary>
    /// Removes action blocks/tags from the response so they are not read aloud or shown in chat.
    /// If the message consisted entirely of action blocks, returns a natural spoken acknowledgment.
    /// </summary>
    public static string StripActionBlocks(string text, IReadOnlyList<ParsedAction>? parsedActions = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string cleaned = FencedBlockRegex.Replace(text, string.Empty);
        cleaned = InlineActionRegex.Replace(cleaned, string.Empty);

        // If the remaining string is a raw JSON object or array, remove it
        string trimmed = cleaned.Trim();
        if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
            (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
        {
            cleaned = string.Empty;
        }
        else
        {
            cleaned = EmbeddedJsonRegex.Replace(cleaned, string.Empty);
        }

        // Collapse multiple blank lines left behind
        cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n").Trim();

        // If nothing remains except whitespace or punctuation, provide a clean natural message
        if (string.IsNullOrWhiteSpace(cleaned) && parsedActions != null && parsedActions.Count > 0)
        {
            var first = parsedActions[0];
            return GenerateNaturalConfirmation(first);
        }

        return cleaned;
    }

    public static string GenerateNaturalConfirmation(ParsedAction action)
    {
        string app = action.App ?? action.Target ?? "the application";
        return action.ActionType.ToLowerInvariant() switch
        {
            "launch_app" or "open_app" or "open" or "launch" => $"Opening {app}...",
            "switch_window" or "switch_app" or "focus_app"   => $"Switching to {app}...",
            "close_window" or "close_app"                   => $"Closing {app}...",
            "maximize"                                       => $"Maximizing {app}...",
            "minimize"                                       => $"Minimizing {app}...",
            "type_text" or "type" or "input"                => $"Typing text...",
            "press_key" or "key" or "shortcut"               => $"Pressing {action.Key}...",
            "click" or "left_click"                          => $"Clicking {action.Target ?? "target"}...",
            _                                                => $"Executing {action.ActionType}..."
        };
    }

    private static List<ParsedAction> TryParseJsonArray(string json)
    {
        var list = new List<ParsedAction>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var a = ParseElement(el);
                    if (a != null) list.Add(a);
                }
            }
        }
        catch { }
        return list;
    }

    private static ParsedAction? TryParseJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ParseElement(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static ParsedAction? ParseElement(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        // Find action property (case-insensitive)
        string actionType = string.Empty;
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name.Equals("action", StringComparison.OrdinalIgnoreCase) ||
                prop.Name.Equals("action_type", StringComparison.OrdinalIgnoreCase) ||
                prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase))
            {
                actionType = prop.Value.GetString() ?? string.Empty;
                break;
            }
        }

        if (string.IsNullOrEmpty(actionType)) return null;

        var parsed = new ParsedAction { ActionType = actionType };

        foreach (var prop in root.EnumerateObject())
        {
            string name = prop.Name.ToLowerInvariant();
            if (name is "app" or "application" or "app_name") parsed.App = prop.Value.GetString();
            else if (name is "text" or "input" or "value") parsed.Text = prop.Value.GetString();
            else if (name is "key" or "shortcut" or "keys") parsed.Key = prop.Value.GetString();
            else if (name is "url" or "uri" or "link") parsed.Url = prop.Value.GetString();
            else if (name is "target" or "target_name" or "element") parsed.Target = prop.Value.GetString();
            else if (name is "query" or "search") parsed.Query = prop.Value.GetString();
            else if (name is "amount" && prop.Value.TryGetInt32(out int amtVal)) parsed.Amount = amtVal;
        }

        return parsed;
    }
}

/// <summary>A structured action parsed from an AI response action block.</summary>
public sealed class ParsedAction
{
    public string ActionType { get; set; } = string.Empty;
    public string? App    { get; set; }
    public string? Text   { get; set; }
    public string? Key    { get; set; }
    public string? Url    { get; set; }
    public string? Target { get; set; }
    public string? Query  { get; set; }
    public int     Amount { get; set; }

    /// <summary>Maps the AI action type to the AutomationEngine action string.</summary>
    public string ToAutomationActionType() => ActionType.ToLowerInvariant() switch
    {
        "launch_app" or "open_app" or "open" or "launch" => "launchapplication",
        "type_text"  or "type"     or "input"            => "typetext",
        "press_key"  or "key"      or "shortcut"         => "shortcut",
        "click"      or "left_click"                     => "leftclick",
        "right_click"                                    => "rightclick",
        "double_click"                                   => "doubleclick",
        "scroll_up"  or "scroll_down"                    => "scroll",
        "switch_window" or "switch_app" or "focus_app"   => "switchwindow",
        "close_window"  or "close_app"                   => "closewindow",
        "maximize"                                       => "maximizewindow",
        "minimize"                                       => "minimizewindow",
        _                                                => ActionType.ToLowerInvariant()
    };

    /// <summary>Returns the best parameter value for the AutomationEngine to consume.</summary>
    public string? GetParameter() => ActionType.ToLowerInvariant() switch
    {
        "launch_app" or "open_app" or "open" or "launch"                                   => App,
        "type_text"  or "type"     or "input"                                              => Text,
        "press_key"  or "key"      or "shortcut"                                           => Key,
        "switch_window" or "switch_app" or "focus_app" or
        "close_window"  or "close_app"  or "maximize"  or "minimize"                       => App,
        "scroll_down"                                                                      => "-120",
        "scroll_up"                                                                        => "120",
        _                                                                                  => Text ?? App ?? Key
    };
}
