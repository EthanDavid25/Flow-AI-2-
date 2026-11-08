using System.IO;
using FlowAI.Audio.Capture;
using FlowAI.Automation;
using FlowAI.Automation.Engine;
using FlowAI.Core.Models;
using FlowAI.Core.Parsing;
using FlowAI.Vision.Discovery;
using Xunit;

namespace FlowAI.Integration.Tests;

/// <summary>
/// Tests for ApplicationDiscovery, ActionJsonParser, DeterministicCommandRouter,
/// WAV audio wrapping, and WindowsAutomationEngine.
/// </summary>
public class AutomationAndParsingTests
{
    // â”€â”€ ActionJsonParser tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ActionJsonParser_ParsesExactNotepadPlainJson()
    {
        // Exact response observed from Ollama
        string response = "{\"action\": \"launch_app\", \"app\": \"Notepad\"}";

        var actions = ActionJsonParser.ParseActions(response);

        Assert.Single(actions);
        Assert.Equal("launch_app", actions[0].ActionType);
        Assert.Equal("Notepad", actions[0].App);
    }

    [Fact]
    public void ActionJsonParser_ParsesGenericMarkdownJsonBlock()
    {
        string response = """
            Here is the action:
            ```json
            {"action": "launch_app", "app": "Notepad"}
            ```
            """;

        var actions = ActionJsonParser.ParseActions(response);

        Assert.Single(actions);
        Assert.Equal("launch_app", actions[0].ActionType);
        Assert.Equal("Notepad", actions[0].App);
    }

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
    public void ActionJsonParser_ParsesJsonArray_MultipleActions()
    {
        string response = """
            [
              {"action": "launch_app", "app": "Google Chrome"},
              {"action": "press_key", "key": "Ctrl+T"}
            ]
            """;

        var actions = ActionJsonParser.ParseActions(response);

        Assert.Equal(2, actions.Count);
        Assert.Equal("launch_app", actions[0].ActionType);
        Assert.Equal("Google Chrome", actions[0].App);
        Assert.Equal("press_key", actions[1].ActionType);
        Assert.Equal("Ctrl+T", actions[1].Key);
    }

    [Fact]
    public void ActionJsonParser_ParsesEmbeddedJsonInText()
    {
        string response = "I am ready to help. {\"action\": \"launch_app\", \"app\": \"Calculator\"} Please wait.";

        var actions = ActionJsonParser.ParseActions(response);

        Assert.Single(actions);
        Assert.Equal("launch_app", actions[0].ActionType);
        Assert.Equal("Calculator", actions[0].App);
    }

    [Fact]
    public void ActionJsonParser_ReturnsEmpty_WhenNoActions()
    {
        string response = "The Settings button is at [POINT:100,200:Settings] in the top right.";
        var actions = ActionJsonParser.ParseActions(response);
        Assert.Empty(actions);
    }

    [Fact]
    public void ActionJsonParser_StripActionBlocks_GeneratesNaturalConfirmation_WhenOnlyJson()
    {
        string response = "{\"action\": \"launch_app\", \"app\": \"Notepad\"}";
        var actions = ActionJsonParser.ParseActions(response);

        string cleanText = ActionJsonParser.StripActionBlocks(response, actions);

        Assert.DoesNotContain("{", cleanText);
        Assert.DoesNotContain("action", cleanText);
        Assert.Equal("Opening Notepad...", cleanText);
    }

    [Fact]
    public void ActionJsonParser_StripActionBlocks_PreservesSurroundingConversation()
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

    // â”€â”€ DeterministicCommandRouter tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Theory]
    [InlineData("Open Notepad", "notepad")]
    [InlineData("open notepad.", "notepad")]
    [InlineData("launch Notepad", "notepad")]
    [InlineData("FlowAI, please open Notepad", "notepad")]
    [InlineData("Start Chrome", "chrome")]
    [InlineData("Open Calculator", "calculator")]
    [InlineData("Open File Explorer", "file explorer")]
    public void DeterministicCommandRouter_RoutesBasicOpenCommands(string query, string expectedApp)
    {
        var plan = DeterministicCommandRouter.TryRoute(query);

        Assert.NotNull(plan);
        Assert.True(plan.IsMatched);
        Assert.Single(plan.Actions);
        Assert.Equal("launchapplication", plan.Actions[0].ActionType);
        Assert.Equal(expectedApp, plan.Actions[0].Parameter, ignoreCase: true);
        Assert.Contains(expectedApp, plan.SpokenConfirmation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeterministicCommandRouter_RoutesSwitchCommand()
    {
        var plan = DeterministicCommandRouter.TryRoute("Switch to Notepad");

        Assert.NotNull(plan);
        Assert.True(plan.IsMatched);
        Assert.Single(plan.Actions);
        Assert.Equal("switchwindow", plan.Actions[0].ActionType);
        Assert.Equal("notepad", plan.Actions[0].Parameter, ignoreCase: true);
    }

    [Fact]
    public void DeterministicCommandRouter_RoutesCloseCommand()
    {
        var plan = DeterministicCommandRouter.TryRoute("Close Chrome");

        Assert.NotNull(plan);
        Assert.True(plan.IsMatched);
        Assert.Single(plan.Actions);
        Assert.Equal("closewindow", plan.Actions[0].ActionType);
        Assert.Equal("chrome", plan.Actions[0].Parameter, ignoreCase: true);
    }

    [Fact]
    public void DeterministicCommandRouter_RoutesComposite_OpenAndType()
    {
        var plan = DeterministicCommandRouter.TryRoute("Open Notepad and type Hello from FlowAI");

        Assert.NotNull(plan);
        Assert.True(plan.IsMatched);
        Assert.Equal(2, plan.Actions.Count);
        Assert.Equal("launchapplication", plan.Actions[0].ActionType);
        Assert.Equal("notepad", plan.Actions[0].Parameter, ignoreCase: true);
        Assert.Equal("typetext", plan.Actions[1].ActionType);
        Assert.Equal("Hello from FlowAI", plan.Actions[1].Parameter);
    }

    [Fact]
    public void DeterministicCommandRouter_RoutesComposite_ChromeSearch()
    {
        var plan = DeterministicCommandRouter.TryRoute("Open Chrome and search for artificial intelligence");

        Assert.NotNull(plan);
        Assert.True(plan.IsMatched);
        Assert.Equal(4, plan.Actions.Count);
        Assert.Equal("launchapplication", plan.Actions[0].ActionType);
        Assert.Equal("shortcut", plan.Actions[1].ActionType); // Ctrl+L
        Assert.Equal("typetext", plan.Actions[2].ActionType);
        Assert.Equal("artificial intelligence", plan.Actions[2].Parameter);
        Assert.Equal("shortcut", plan.Actions[3].ActionType); // Enter
    }

    [Fact]
    public void DeterministicCommandRouter_ReturnsNull_ForOpenEndedQuestion()
    {
        var plan = DeterministicCommandRouter.TryRoute("Where is the save button on the screen?");
        Assert.Null(plan);
    }

    // â”€â”€ ApplicationDiscovery tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
        var discovery = new ApplicationDiscovery();
        string? path = discovery.FindApplication("chrome");

        if (path != null)
        {
            Assert.True(File.Exists(path), $"Chrome path does not exist: {path}");
        }
    }

    // â”€â”€ WAV audio wrapping test (Whisper fix) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void WasapiAudioRecorder_WrapInWavHeader_GeneratesValidRiffHeader()
    {
        byte[] dummyPcm = new byte[3200]; // 100ms of 16kHz 16-bit mono audio
        for (int i = 0; i < dummyPcm.Length; i++) dummyPcm[i] = (byte)(i % 256);

        byte[] wavBytes = WasapiAudioRecorder.WrapInWavHeader(dummyPcm, 16000, 1, 16);

        Assert.Equal(dummyPcm.Length + 44, wavBytes.Length);
        // "RIFF"
        Assert.Equal((byte)'R', wavBytes[0]);
        Assert.Equal((byte)'I', wavBytes[1]);
        Assert.Equal((byte)'F', wavBytes[2]);
        Assert.Equal((byte)'F', wavBytes[3]);
        // "WAVE"
        Assert.Equal((byte)'W', wavBytes[8]);
        Assert.Equal((byte)'A', wavBytes[9]);
        Assert.Equal((byte)'V', wavBytes[10]);
        Assert.Equal((byte)'E', wavBytes[11]);
        // "fmt "
        Assert.Equal((byte)'f', wavBytes[12]);
        Assert.Equal((byte)'m', wavBytes[13]);
        Assert.Equal((byte)'t', wavBytes[14]);
        Assert.Equal((byte)' ', wavBytes[15]);
        // "data"
        Assert.Equal((byte)'d', wavBytes[36]);
        Assert.Equal((byte)'a', wavBytes[37]);
        Assert.Equal((byte)'t', wavBytes[38]);
        Assert.Equal((byte)'a', wavBytes[39]);
    }

    // â”€â”€ WindowsAutomationEngine new actions tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task WindowsAutomationEngine_RejectsUnsupportedAction_Gracefully()
    {
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

    [Theory]
    [InlineData("Hello FlowAI")]
    [InlineData("hello")]
    [InlineData("Hi")]
    [InlineData("hey")]
    public void DeterministicCommandRouter_RoutesGreetings(string query)
    {
        var plan = DeterministicCommandRouter.TryRoute(query);
        Assert.NotNull(plan);
        Assert.True(plan.IsMatched);
        Assert.Equal("Greeting", plan.Description);
        Assert.Contains("Hello", plan.UserFacingMessage);
    }

    [Theory]
    [InlineData("search something on google", "something")]
    [InlineData("google weather today", "weather today")]
    [InlineData("search for quantum computing", "quantum computing")]
    public void DeterministicCommandRouter_RoutesDirectWebSearch(string query, string expectedSearch)
    {
        var plan = DeterministicCommandRouter.TryRoute(query);
        Assert.NotNull(plan);
        Assert.True(plan.IsMatched);
        Assert.Equal(4, plan.Actions.Count);
        Assert.Equal("launchapplication", plan.Actions[0].ActionType);
        Assert.Equal("typetext", plan.Actions[2].ActionType);
        Assert.Equal(expectedSearch, plan.Actions[2].Parameter);
    }

    [Fact]
    public void DeterministicCommandRouter_RoutesValorantAndClaude()
    {
        var valorantPlan = DeterministicCommandRouter.TryRoute("Open Valorant");
        Assert.NotNull(valorantPlan);
        Assert.True(valorantPlan.IsMatched);
        Assert.Equal("launchapplication", valorantPlan.Actions[0].ActionType);
        Assert.Equal("Valorant", valorantPlan.Actions[0].Parameter);

        var claudePlan = DeterministicCommandRouter.TryRoute("Open Claude");
        Assert.NotNull(claudePlan);
        Assert.True(claudePlan.IsMatched);
        Assert.Equal("launchapplication", claudePlan.Actions[0].ActionType);
        Assert.Equal("Claude", claudePlan.Actions[0].Parameter);
    }

    [Fact]
    public void ApplicationDiscovery_FindsGoogleChrome()
    {
        var discovery = new ApplicationDiscovery();
        string? chromePath = discovery.FindApplication("chrome");
        Assert.NotNull(chromePath);
        Assert.True(File.Exists(chromePath));
        Assert.Contains("chrome.exe", chromePath, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Open North Park.", "notepad")]
    [InlineData("open calculator. calculator", "calculator")]
    [InlineData("Open Note pad", "notepad")]
    [InlineData("open crome", "google chrome")]
    [InlineData("open valorent", "valorant")]
    [InlineData("open claud desktop", "claude")]
    public void DeterministicCommandRouter_NormalizesSpokenAppAliases(string query, string expectedApp)
    {
        var plan = DeterministicCommandRouter.TryRoute(query);
        Assert.NotNull(plan);
        Assert.True(plan.IsMatched);
        Assert.Equal("launchapplication", plan.Actions[0].ActionType);
        Assert.Equal(expectedApp, plan.Actions[0].Parameter, ignoreCase: true);
    }
}
