using System.Text.RegularExpressions;
using FlowAI.Core.Models;

namespace FlowAI.Automation;

/// <summary>
/// A fast, safe, deterministic local intent router for unambiguous desktop commands.
/// Bypasses both cloud and local LLM inference for common application and window operations,
/// providing instantaneous execution, zero network latency, and complete offline reliability.
/// </summary>
public static class DeterministicCommandRouter
{
    public sealed class DeterministicPlan
    {
        public bool IsMatched { get; set; }
        public string Description { get; set; } = string.Empty;
        public string UserFacingMessage { get; set; } = string.Empty;
        public string SpokenConfirmation { get; set; } = string.Empty;
        public List<AutomationAction> Actions { get; set; } = new();
    }

    /// <summary>
    /// Attempts to route a user query deterministically.
    /// Returns null if the query is not a supported deterministic command (should go to AI).
    /// </summary>
    public static DeterministicPlan? TryRoute(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText)) return null;

        string normalized = CleanInput(userText);
        if (string.IsNullOrEmpty(normalized)) return null;

        // ── 0. Greetings & wake words ─────────────────────────────────────────
        if (normalized.Equals("hello", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("hi", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("hey", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("hello flowai", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("hey flowai", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("good morning", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("good afternoon", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("good evening", StringComparison.OrdinalIgnoreCase))
        {
            return new DeterministicPlan
            {
                IsMatched = true,
                Description = "Greeting",
                UserFacingMessage = "Hello! I'm FlowAI, your desktop assistant. How can I help you?",
                SpokenConfirmation = "Hello! How can I help you?",
                Actions = []
            };
        }

        // ── 1. Composite: Open Chrome and search for <query> ───────────────────
        var chromeSearchMatch = Regex.Match(normalized,
            @"^(?:open|launch|start)\s+(?:google\s+)?chrome\s+(?:and\s+)?(?:search\s+for|search|google)\s+(.+)$",
            RegexOptions.IgnoreCase);

        if (chromeSearchMatch.Success)
        {
            string query = chromeSearchMatch.Groups[1].Value.Trim();
            var plan = new DeterministicPlan
            {
                IsMatched = true,
                Description = $"Open Chrome and search for '{query}'",
                UserFacingMessage = $"Opening Chrome and searching for \"{query}\"...",
                SpokenConfirmation = $"Searching for {query} in Chrome."
            };

            plan.Actions.Add(new AutomationAction
            {
                ActionType = "launchapplication",
                Description = "Open Google Chrome",
                Parameter = "chrome",
                RiskLevel = ActionRiskLevel.Low,
                UserConfirmed = true
            });

            plan.Actions.Add(new AutomationAction
            {
                ActionType = "shortcut",
                Description = "Focus address bar",
                Parameter = "Ctrl+L",
                RiskLevel = ActionRiskLevel.Low,
                UserConfirmed = true
            });

            plan.Actions.Add(new AutomationAction
            {
                ActionType = "typetext",
                Description = $"Type search query",
                Parameter = query,
                RiskLevel = ActionRiskLevel.Low,
                UserConfirmed = true
            });

            plan.Actions.Add(new AutomationAction
            {
                ActionType = "shortcut",
                Description = "Submit search",
                Parameter = "Enter",
                RiskLevel = ActionRiskLevel.Low,
                UserConfirmed = true
            });

            return plan;
        }

        // ── 1b. Standalone search: "search for <query>", "search <query> on google", "google <query>"
        var directSearchMatch = Regex.Match(normalized,
            @"^(?:search\s+for|search|google)\s+(.+?)(?:\s+on\s+google|\s+in\s+google)?$",
            RegexOptions.IgnoreCase);

        if (directSearchMatch.Success && !normalized.StartsWith("open", StringComparison.OrdinalIgnoreCase))
        {
            string query = directSearchMatch.Groups[1].Value.Trim();
            var plan = new DeterministicPlan
            {
                IsMatched = true,
                Description = $"Search Google for '{query}'",
                UserFacingMessage = $"Searching Google for \"{query}\"...",
                SpokenConfirmation = $"Searching for {query} on Google."
            };

            plan.Actions.Add(new AutomationAction
            {
                ActionType = "launchapplication",
                Description = "Open Google Chrome",
                Parameter = "chrome",
                RiskLevel = ActionRiskLevel.Low,
                UserConfirmed = true
            });

            plan.Actions.Add(new AutomationAction
            {
                ActionType = "shortcut",
                Description = "Focus address bar",
                Parameter = "Ctrl+L",
                RiskLevel = ActionRiskLevel.Low,
                UserConfirmed = true
            });

            plan.Actions.Add(new AutomationAction
            {
                ActionType = "typetext",
                Description = "Type search query",
                Parameter = query,
                RiskLevel = ActionRiskLevel.Low,
                UserConfirmed = true
            });

            plan.Actions.Add(new AutomationAction
            {
                ActionType = "shortcut",
                Description = "Submit search",
                Parameter = "Enter",
                RiskLevel = ActionRiskLevel.Low,
                UserConfirmed = true
            });

            return plan;
        }

        // ── 2. Composite: Open <app> and type/calculate <text> ────────────────
        var openAndTypeMatch = Regex.Match(normalized,
            @"^(?:open|launch|start)\s+([a-z0-9\+\.\s]+?)\s+(?:and\s+)?(?:type|write|enter|perform|calculate|do)\s+(.+)$",
            RegexOptions.IgnoreCase);

        if (openAndTypeMatch.Success)
        {
            string app = openAndTypeMatch.Groups[1].Value.Trim();
            string textToType = openAndTypeMatch.Groups[2].Value.Trim();

            // Ignore if app captured "chrome and search" (handled above)
            if (!app.StartsWith("chrome and search", StringComparison.OrdinalIgnoreCase))
            {
                var plan = new DeterministicPlan
                {
                    IsMatched = true,
                    Description = $"Open {app} and type text",
                    UserFacingMessage = $"Opening {app} and typing text...",
                    SpokenConfirmation = $"Opened {app} and entered your text."
                };

                plan.Actions.Add(new AutomationAction
                {
                    ActionType = "launchapplication",
                    Description = $"Open {app}",
                    Parameter = app,
                    RiskLevel = ActionRiskLevel.Low,
                    UserConfirmed = true
                });

                plan.Actions.Add(new AutomationAction
                {
                    ActionType = "typetext",
                    Description = $"Type into {app}",
                    Parameter = textToType,
                    RiskLevel = ActionRiskLevel.Low,
                    UserConfirmed = true
                });

                return plan;
            }
        }

        // ── 3. Switch to <app> / Focus <app> ──────────────────────────────────
        var switchMatch = Regex.Match(normalized,
            @"^(?:switch\s+to|activate|focus|bring\s+up|show)\s+(.+)$",
            RegexOptions.IgnoreCase);

        if (switchMatch.Success)
        {
            string app = CleanAppName(switchMatch.Groups[1].Value);
            return new DeterministicPlan
            {
                IsMatched = true,
                Description = $"Switch to {app}",
                UserFacingMessage = $"Switching to {app}...",
                SpokenConfirmation = $"Switched to {app}.",
                Actions =
                [
                    new AutomationAction
                    {
                        ActionType = "switchwindow",
                        Description = $"Switch to {app}",
                        Parameter = app,
                        RiskLevel = ActionRiskLevel.Low,
                        UserConfirmed = true
                    }
                ]
            };
        }

        // ── 4. Close <app> / Quit <app> ───────────────────────────────────────
        var closeMatch = Regex.Match(normalized,
            @"^(?:close|quit|exit|kill)\s+(.+)$",
            RegexOptions.IgnoreCase);

        if (closeMatch.Success)
        {
            string app = CleanAppName(closeMatch.Groups[1].Value);
            return new DeterministicPlan
            {
                IsMatched = true,
                Description = $"Close {app}",
                UserFacingMessage = $"Closing {app}...",
                SpokenConfirmation = $"Closed {app}.",
                Actions =
                [
                    new AutomationAction
                    {
                        ActionType = "closewindow",
                        Description = $"Close {app}",
                        Parameter = app,
                        RiskLevel = ActionRiskLevel.Medium,
                        UserConfirmed = true
                    }
                ]
            };
        }

        // ── 5. Maximize / Minimize window ─────────────────────────────────────
        var maxMatch = Regex.Match(normalized, @"^maximize(?:\s+(.+))?$", RegexOptions.IgnoreCase);
        if (maxMatch.Success)
        {
            string app = CleanAppName(maxMatch.Groups[1].Value);
            return new DeterministicPlan
            {
                IsMatched = true,
                Description = $"Maximize {app}",
                UserFacingMessage = "Maximizing window...",
                SpokenConfirmation = "Window maximized.",
                Actions =
                [
                    new AutomationAction
                    {
                        ActionType = "maximizewindow",
                        Description = "Maximize window",
                        Parameter = app,
                        RiskLevel = ActionRiskLevel.Low,
                        UserConfirmed = true
                    }
                ]
            };
        }

        var minMatch = Regex.Match(normalized, @"^minimize(?:\s+(.+))?$", RegexOptions.IgnoreCase);
        if (minMatch.Success)
        {
            string app = CleanAppName(minMatch.Groups[1].Value);
            return new DeterministicPlan
            {
                IsMatched = true,
                Description = $"Minimize {app}",
                UserFacingMessage = "Minimizing window...",
                SpokenConfirmation = "Window minimized.",
                Actions =
                [
                    new AutomationAction
                    {
                        ActionType = "minimizewindow",
                        Description = "Minimize window",
                        Parameter = app,
                        RiskLevel = ActionRiskLevel.Low,
                        UserConfirmed = true
                    }
                ]
            };
        }

        // ── 6. Simple Launch: Open <app> / Launch <app> / Start <app> ─────────
        var openMatch = Regex.Match(normalized,
            @"^(?:open|launch|start|run)\s+(.+)$",
            RegexOptions.IgnoreCase);

        if (openMatch.Success)
        {
            string app = CleanAppName(openMatch.Groups[1].Value);
            if (!string.IsNullOrEmpty(app))
            {
                // If it still contains " and ", it is a complex compound instruction that should be handled by AI
                if (app.Contains(" and ", StringComparison.OrdinalIgnoreCase)) return null;

                return new DeterministicPlan
                {
                    IsMatched = true,
                    Description = $"Open {app}",
                    UserFacingMessage = $"Opening {app}...",
                    SpokenConfirmation = $"Opened {app}.",
                    Actions =
                    [
                        new AutomationAction
                        {
                            ActionType = "launchapplication",
                            Description = $"Launch {app}",
                            Parameter = app,
                            RiskLevel = ActionRiskLevel.Low,
                            UserConfirmed = true
                        }
                    ]
                };
            }
        }

        return null;
    }

    private static string CleanInput(string input)
    {
        string text = input.Trim();

        // Strip wake words and pleasantries
        string[] wakePrefixes =
        [
            "flowai,", "flowai", "hey flowai,", "hey flowai",
            "please", "can you please", "can you", "could you please", "could you"
        ];

        foreach (var prefix in wakePrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(prefix.Length).Trim();
            }
        }

        // Strip trailing punctuation
        text = text.TrimEnd('.', '!', '?', ';');
        return text;
    }

    private static string CleanAppName(string raw)
    {
        string name = raw.Trim().TrimEnd('.', '!', '?');
        if (name.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(4).Trim();
        if (name.EndsWith(" app", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4).Trim();
        if (name.EndsWith(" application", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 12).Trim();
        return name;
    }
}
