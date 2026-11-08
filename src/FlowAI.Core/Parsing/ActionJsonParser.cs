using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlowAI.Core.Parsing;

/// <summary>
/// Parses structured action JSON blocks from AI responses.
///
/// The AI embeds action intents in its response in one of two forms:
///
///   Fenced block (preferred):
///     ```action
///     {"action": "launch_app", "app": "Google Chrome"}
///     ```
///
///   Inline tag (compact):
///     [ACTION:{"action":"type_text","text":"Hello World"}]
///
/// These are stripped from the spoken text so the user hears only the natural-language part.
/// </summary>
public static class ActionJsonParser
{
    // Matches ```action ... ``` code fences
    private static readonly Regex FencedActionBlock = new(
        @"```action\s*\r?\n([\s\S]*?)\r?\n?\s*```",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches [ACTION:{...}] inline tags
    private static readonly Regex InlineActionTag = new(
        @"\[ACTION:([\s\S]*?)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Extracts all action blocks from an AI response text.</summary>
    public static List<ParsedAction> ParseActions(string text)
    {
        var actions = new List<ParsedAction>();

        foreach (Match m in FencedActionBlock.Matches(text))
        {
            var action = TryParseJson(m.Groups[1].Value.Trim());
            if (action != null) actions.Add(action);
        }

        foreach (Match m in InlineActionTag.Matches(text))
        {
            var action = TryParseJson(m.Groups[1].Value.Trim());
            if (action != null) actions.Add(action);
        }

        return actions;
    }

    /// <summary>
    /// Removes action blocks/tags from the response so they are not read aloud.
    /// Call after ParseActions to get the clean spoken text.
    /// </summary>
    public static string StripActionBlocks(string text)
    {
        text = FencedActionBlock.Replace(text, string.Empty);
        text = InlineActionTag.Replace(text, string.Empty);
        // Collapse multiple blank lines left behind
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static ParsedAction? TryParseJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("action", out var actionProp)) return null;
            string actionType = actionProp.GetString() ?? string.Empty;
            if (string.IsNullOrEmpty(actionType)) return null;

            var parsed = new ParsedAction { ActionType = actionType };

            if (root.TryGetProperty("app", out var app))    parsed.App    = app.GetString();
            if (root.TryGetProperty("text", out var text))  parsed.Text   = text.GetString();
            if (root.TryGetProperty("key", out var key))    parsed.Key    = key.GetString();
            if (root.TryGetProperty("url", out var url))    parsed.Url    = url.GetString();
            if (root.TryGetProperty("target", out var tgt)) parsed.Target = tgt.GetString();
            if (root.TryGetProperty("query", out var qry))  parsed.Query  = qry.GetString();
            if (root.TryGetProperty("amount", out var amt) && amt.TryGetInt32(out int amtVal))
                parsed.Amount = amtVal;

            return parsed;
        }
        catch
        {
            return null;
        }
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
