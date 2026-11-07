using FlowAI.Core.Configuration;
using FlowAI.Core.Enums;
using FlowAI.Core.Geometry;
using FlowAI.Core.Models;
using FlowAI.Core.Parsing;
using FlowAI.Core.StateMachine;
using Xunit;

namespace FlowAI.Core.Tests;

public class FlowAICoreTests
{
    [Fact]
    public void StateMachine_TransitionsCorrectly_AndFiresEvent()
    {
        var stateManager = new FlowAIStateManager();
        FlowAIState? reportedOld = null;
        FlowAIState? reportedNew = null;

        stateManager.StateChanged += (s, e) =>
        {
            reportedOld = e.PreviousState;
            reportedNew = e.NewState;
        };

        Assert.Equal(FlowAIState.Idle, stateManager.CurrentState);

        bool transitioned = stateManager.TransitionTo(FlowAIState.Listening, "User pressed hotkey");
        Assert.True(transitioned);
        Assert.Equal(FlowAIState.Listening, stateManager.CurrentState);
        Assert.Equal(FlowAIState.Idle, reportedOld);
        Assert.Equal(FlowAIState.Listening, reportedNew);

        // Attempting to transition to the same state should return false
        bool sameTransition = stateManager.TransitionTo(FlowAIState.Listening);
        Assert.False(sameTransition);

        stateManager.TransitionTo(FlowAIState.Processing);
        Assert.Equal(FlowAIState.Processing, stateManager.CurrentState);

        stateManager.ResetToIdle();
        Assert.Equal(FlowAIState.Idle, stateManager.CurrentState);
    }

    [Fact]
    public void PointProtocolParser_ParsesPointTags_AndCleansSpokenText()
    {
        string rawResponse = "Here is the submit button [POINT:450,220:Submit Button:screen0] that you asked for.";
        var targets = PointProtocolParser.ParseTargets(rawResponse, out string cleanText);

        Assert.Single(targets);
        var target = targets[0];
        Assert.Equal(450, target.TargetPoint.X);
        Assert.Equal(220, target.TargetPoint.Y);
        Assert.Equal("Submit Button", target.Label);
        Assert.Equal(0, target.ScreenIndex);
        Assert.Equal("Here is the submit button that you asked for.", cleanText);
    }

    [Fact]
    public void PointProtocolParser_ParsesBoxTags_AndComputesCenter()
    {
        string rawResponse = "Look here [BOX:100,200,80,40:Search Bar:screen1] for search.";
        var targets = PointProtocolParser.ParseTargets(rawResponse, out string cleanText);

        Assert.Single(targets);
        var target = targets[0];
        Assert.Equal(140, target.TargetPoint.X); // Center X = 100 + 40
        Assert.Equal(220, target.TargetPoint.Y); // Center Y = 200 + 20
        Assert.Equal("Search Bar", target.Label);
        Assert.Equal(1, target.ScreenIndex);
        Assert.Equal(80, target.BoundingBox.Width);
        Assert.Equal(40, target.BoundingBox.Height);
        Assert.Equal("Look here for search.", cleanText);
    }

    [Fact]
    public void PointProtocolParser_HandlesMultipleTags_Cleanly()
    {
        string rawResponse = "First check [POINT:120,50:File Menu] then [POINT:340,50:Edit Menu:screen0].";
        var targets = PointProtocolParser.ParseTargets(rawResponse, out string cleanText);

        Assert.Equal(2, targets.Count);
        Assert.Equal("File Menu", targets[0].Label);
        Assert.Equal("Edit Menu", targets[1].Label);
        Assert.Equal("First check then .", cleanText);
    }

    [Fact]
    public void CoordinateTransform_ConvertsDipsAndPixelsAccurately()
    {
        var physical = new CoordinatePoint(300, 450);
        double scale150 = 1.5;

        var dips = CoordinateTransform.PhysicalPixelsToDips(physical, scale150);
        Assert.Equal(200, dips.X);
        Assert.Equal(300, dips.Y);

        var roundTrip = CoordinateTransform.DipsToPhysicalPixels(dips, scale150);
        Assert.Equal(300, roundTrip.X);
        Assert.Equal(450, roundTrip.Y);
    }

    [Fact]
    public void CoordinateTransform_MapsNormalizedCoordinates_WithNegativeOrigins()
    {
        // Multi-monitor scenario: Secondary screen to the left at X = -1920, Y = 0, Width = 1920, Height = 1080
        var secondaryScreen = new ScreenInfo
        {
            ScreenIndex = 1,
            PhysicalBounds = new BoundingBox(-1920, 0, 1920, 1080),
            ScaleFactor = 1.0
        };

        // Center point normalized (0.5, 0.5)
        var screenPoint = CoordinateTransform.NormalizedToScreenPixels(0.5, 0.5, secondaryScreen);
        Assert.Equal(-960, screenPoint.X);
        Assert.Equal(540, screenPoint.Y);
    }

    [Fact]
    public void Settings_DefaultValues_AreCorrect()
    {
        var settings = new FlowAISettings();
        Assert.Equal("Ollama", settings.AIProvider);
        Assert.Equal("qwen3-vl:2b", settings.OllamaModel);
        Assert.Equal("Ctrl+Alt+Space", settings.Hotkey);
        Assert.Equal("#00FF88", settings.GreenAccentHex);
        Assert.True(settings.LearningEnabled);
        Assert.True(settings.RequireConfirmationForConsequential);
        Assert.False(settings.StoreScreenshotsWithWorkflows);
    }
}
