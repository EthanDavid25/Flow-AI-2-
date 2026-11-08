using System.IO;
using FlowAI.Automation;
using FlowAI.Automation.Engine;
using FlowAI.Core.Models;
using FlowAI.Core.Parsing;
using FlowAI.Vision.Discovery;
using Xunit;

namespace FlowAI.Integration.Tests;

/// <summary>
/// Tests for ApplicationDiscovery, ActionJsonParser, and new automation action types.
/// </summary>
public class AutomationAndParsingTests
{
    // ── ActionJsonParser tests ─────────────────────────────────────────────────

    [Fact]
    public void ActionJsonParser_ParsesFencedBlock_LaunchApp()
    {
        string response = """
            I'll open Google Chrome for you.
            ```action
            {"action": "launch_app", "app": "Google Chrome"}
            ```
            Opening Chrome now!
            """;

        var actions = ActionJsonParser.ParseActions(response);

        Assert.Single(actions);
        Assert.Equal("launch_app", actions[0].ActionType);
        Assert.Equal("Google Chrome", actions[0].App);
    }

    [Fact]
    public void ActionJsonParser_ParsesInlineTag_TypeText()
    {
        string response = "Typing for you. [ACTION:{\"action\":\"type_text\",\"text\":\"Hello, FlowAI!\"}]";

        var actions = ActionJsonParser.ParseActions(response);

        Assert.Single(actions);
        Assert.Equal("type_text", actions[0].ActionType);
        Assert.Equal("Hello, FlowAI!", actions[0].Text);
    }

    [Fact]
    public void ActionJsonParser_ParsesMultipleActions()
    {
        string response = """
            Opening Chrome and navigating.
            ```action
            {"action": "launch_app", "app": "Google Chrome"}
            ```
            ```action
            {"action": "press_key", "key": "Ctrl+T"}
            ```
            """;

        var actions = ActionJsonParser.ParseActions(response);
        Assert.Equal(2, actions.Count);
        Assert.Equal("launch_app", actions[0].ActionType);
        Assert.Equal("press_key", actions[1].ActionType);
        Assert.Equal("Ctrl+T", actions[1].Key);
    }

    [Fact]
    public void ActionJsonParser_ReturnsEmpty_WhenNoActions()
    {
        string response = "The Settings button is at [POINT:100,200:Settings] in the top right.";
        var actions = ActionJsonParser.ParseActions(response);
        Assert.Empty(actions);
    }

    [Fact]
    public void ActionJsonParser_StripActionBlocks_RemovesFromText()
    {
        string response = """
            Opening Notepad.
            ```action
            {"action": "launch_app", "app": "Notepad"}
            ```
            Done!
            """;

        string stripped = ActionJsonParser.StripActionBlocks(response);
        Assert.DoesNotContain("```action", stripped);
        Assert.DoesNotContain("launch_app", stripped);
        Assert.Contains("Opening Notepad", stripped);
        Assert.Contains("Done!", stripped);
    }

    [Fact]
    public void ActionJsonParser_MapsActionType_ToAutomationActionType()
    {
        var launchAction = new ParsedAction { ActionType = "launch_app", App = "Chrome" };
        Assert.Equal("launchapplication", launchAction.ToAutomationActionType());

        var typeAction = new ParsedAction { ActionType = "type_text", Text = "hello" };
        Assert.Equal("typetext", typeAction.ToAutomationActionType());

        var scrollAction = new ParsedAction { ActionType = "scroll_down" };
        Assert.Equal("scroll", scrollAction.ToAutomationActionType());
    }

    [Fact]
    public void ParsedAction_GetParameter_ReturnsCorrectValue()
    {
        var launchAction = new ParsedAction { ActionType = "launch_app", App = "Notepad" };
        Assert.Equal("Notepad", launchAction.GetParameter());

        var typeAction = new ParsedAction { ActionType = "type_text", Text = "Hello World" };
        Assert.Equal("Hello World", typeAction.GetParameter());

        var scrollDown = new ParsedAction { ActionType = "scroll_down" };
        Assert.Equal("-120", scrollDown.GetParameter());

        var scrollUp = new ParsedAction { ActionType = "scroll_up" };
        Assert.Equal("120", scrollUp.GetParameter());
    }

    // ── ApplicationDiscovery tests ─────────────────────────────────────────────

    [Fact]
    public void ApplicationDiscovery_FindsNotepad_Always()
    {
        var discovery = new ApplicationDiscovery();
        string? path = discovery.FindApplication("notepad");
        Assert.NotNull(path);
        Assert.True(File.Exists(path), $"Notepad path does not exist: {path}");
        Assert.Contains("notepad", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplicationDiscovery_FindsCalculator_Always()
    {
        var discovery = new ApplicationDiscovery();
        string? path = discovery.FindApplication("calculator");
        Assert.NotNull(path);
        Assert.True(File.Exists(path), $"Calculator path does not exist: {path}");
    }

    [Fact]
    public void ApplicationDiscovery_FindsExplorer_Always()
    {
        var discovery = new ApplicationDiscovery();
        string? path = discovery.FindApplication("explorer");
        Assert.NotNull(path);
        Assert.True(File.Exists(path), $"Explorer path does not exist: {path}");
    }

    [Fact]
    public void ApplicationDiscovery_ReturnsNull_ForUnknownApp()
    {
        var discovery = new ApplicationDiscovery();
        string? path = discovery.FindApplication("xyzAppThatDoesNotExist12345");
        Assert.Null(path);
    }

    [Fact]
    public void ApplicationDiscovery_FindsChrome_WhenInstalled()
    {
        // This test is conditional — Chrome may not be installed in every environment
        var discovery = new ApplicationDiscovery();
        string? path = discovery.FindApplication("chrome");

        if (path != null)
        {
            Assert.True(File.Exists(path), $"Chrome path does not exist: {path}");
        }
        // Test passes even if Chrome is not found (it may not be installed)
    }

    [Fact]
    public void ApplicationDiscovery_FindRunningProcess_ReturnsNullForNonRunningApp()
    {
        var proc = ApplicationDiscovery.FindRunningProcess("xyzFakeApp99999");
        Assert.Null(proc);
    }

    // ── WindowsAutomationEngine new actions tests ──────────────────────────────

    [Fact]
    public async Task WindowsAutomationEngine_RejectsUnsupportedAction_GracefullyBeforeNewActions()
    {
        // This was the existing behavior — now we have new supported actions
        // but still reject truly unknown actions
        var uiDiscovery = new UIAutomationDiscoveryService();
        var engine = new WindowsAutomationEngine(uiDiscovery);

        var unknownAction = new AutomationAction
        {
            ActionType = "teleport",
            Description = "Teleport cursor",
            RiskLevel = ActionRiskLevel.Low,
            UserConfirmed = true
        };

        var result = await engine.ExecuteActionAsync(unknownAction);
        Assert.False(result.Success);
        Assert.Contains("Unsupported action type", result.Message);
    }

    [Fact]
    public async Task WindowsAutomationEngine_LaunchApplicationAction_IsRecognized()
    {
        // Test that "launchapplication" action type is handled (not returned as unsupported)
        // We don't test actual launch (would open windows during test) — just verify routing
        var uiDiscovery = new UIAutomationDiscoveryService();
        var appDiscovery = new ApplicationDiscovery();
        var engine = new WindowsAutomationEngine(uiDiscovery, appDiscovery);

        var launchAction = new AutomationAction
        {
            ActionType = "launchapplication",
            Description = "Open Notepad",
            Parameter = "notepad",          // notepad is always available
            RiskLevel = ActionRiskLevel.Low,
            UserConfirmed = true
        };

        // This actually launches Notepad — close it immediately after
        var result = await engine.ExecuteActionAsync(launchAction);
        Assert.True(result.Success, $"Launch failed: {result.Message}");

        // Clean up: close any notepad that was opened
        await Task.Delay(1500); // wait for Notepad to open
        var notepadProcs = System.Diagnostics.Process.GetProcessesByName("notepad");
        foreach (var p in notepadProcs)
        {
            try { p.CloseMainWindow(); } catch { }
        }
    }
}
