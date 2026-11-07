using System.IO;
using FlowAI.Automation.Engine;
using FlowAI.Core.Configuration;
using FlowAI.Core.Models;
using FlowAI.Learning;
using FlowAI.Memory;
using FlowAI.Vision.Discovery;
using Xunit;

namespace FlowAI.Integration.Tests;

public class FlowAIIntegrationTests
{
    [Fact]
    public async Task SqliteMemoryStore_SavesAndRetrieves_Preferences()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"flowai_test_{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteMemoryStore(tempDb);
            await store.InitializeAsync();

            var pref = new UserPreference
            {
                Key = "TestVoice",
                Value = "en-US-JennyNeural",
                Category = "Voice"
            };

            await store.SavePreferenceAsync(pref);
            string? loaded = await store.GetPreferenceAsync("TestVoice");

            Assert.Equal("en-US-JennyNeural", loaded);
        }
        finally
        {
            TryDeleteFile(tempDb);
        }
    }

    [Fact]
    public async Task LearningEngine_RecordsAndRetrieves_TaughtWorkflow()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"flowai_test_wf_{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteMemoryStore(tempDb);
            await store.InitializeAsync();
            var engine = new LearningEngine(store);

            engine.StartTeachingSession("Export Document", "Exports current doc as PDF", "WordPad");
            Assert.True(engine.IsTeachingSessionActive);

            engine.RecordTeachingStep(new WorkflowStep
            {
                ActionType = "Click",
                TargetDescription = "File Menu"
            });
            engine.RecordTeachingStep(new WorkflowStep
            {
                ActionType = "Click",
                TargetDescription = "Print / Export"
            });

            var savedWf = await engine.FinishTeachingSessionAsync();
            Assert.False(engine.IsTeachingSessionActive);
            Assert.Equal("Export Document", savedWf.Name);
            Assert.Equal(2, savedWf.Steps.Count);

            // Re-fetch from store to verify persistence
            var retrieved = await store.GetWorkflowByNameAsync("Export Document");
            Assert.NotNull(retrieved);
            Assert.Equal(2, retrieved.Steps.Count);
            Assert.Equal("File Menu", retrieved.Steps[0].TargetDescription);
        }
        finally
        {
            TryDeleteFile(tempDb);
        }
    }

    [Fact]
    public async Task LearningEngine_LearnsCorrection_AndReturnsCorrectedTarget()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"flowai_test_corr_{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteMemoryStore(tempDb);
            await store.InitializeAsync();
            var engine = new LearningEngine(store);

            var correctTarget = new DetectedTarget(650, 320, "Save As Button")
            {
                AutomationId = "SaveAsBtn",
                ControlType = "Button"
            };

            await engine.RecordCorrectionAsync(
                "Notepad",
                "Untitled - Notepad",
                "save file",
                "Save Button",
                correctTarget);

            // Now check if FlowAI retrieves the learned correction
            var learned = await engine.CheckForLearnedCorrectionAsync("Notepad", "save file");
            Assert.NotNull(learned);
            Assert.Equal("Save As Button", learned.Label);
            Assert.Equal(650, learned.TargetPoint.X);
            Assert.Equal(320, learned.TargetPoint.Y);
        }
        finally
        {
            TryDeleteFile(tempDb);
        }
    }

    [Fact]
    public async Task AutomationEngine_BlocksHighRiskAction_WhenUnconfirmed()
    {
        var discovery = new UIAutomationDiscoveryService();
        var engine = new WindowsAutomationEngine(discovery);

        var highRiskAction = new AutomationAction
        {
            ActionType = "Click",
            Description = "Submit order and charge card",
            RiskLevel = ActionRiskLevel.High,
            UserConfirmed = false
        };

        var result = await engine.ExecuteActionAsync(highRiskAction);

        Assert.False(result.Success);
        Assert.Contains("requires user confirmation", result.Message);
    }

    [Fact]
    public async Task SqliteMemoryStore_DeletesCorrection_AndPreferences()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"flowai_test_del_{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteMemoryStore(tempDb);
            await store.InitializeAsync();

            await store.SavePreferenceAsync(new UserPreference { Key = "Pref1", Value = "Val1" });
            var prefs = await store.GetAllPreferencesAsync();
            Assert.Single(prefs);

            await store.DeletePreferenceAsync("Pref1");
            prefs = await store.GetAllPreferencesAsync();
            Assert.Empty(prefs);

            await store.SaveCorrectionAsync(new CorrectionRecord
            {
                ApplicationName = "App1",
                UserQuery = "test query",
                CorrectedTargetLabel = "Target"
            });
            var corrs = await store.GetAllCorrectionsAsync();
            Assert.Single(corrs);

            await store.DeleteCorrectionAsync(corrs[0].Id);
            corrs = await store.GetAllCorrectionsAsync();
            Assert.Empty(corrs);
        }
        finally
        {
            TryDeleteFile(tempDb);
        }
    }

    [Fact]
    public async Task SqliteMemoryStore_ClearsAllData()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"flowai_test_clear_{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteMemoryStore(tempDb);
            await store.InitializeAsync();

            await store.SavePreferenceAsync(new UserPreference { Key = "P", Value = "V" });
            await store.SaveFactAsync(new LearnedFact { Fact = "F", Context = "C" });

            await store.ClearAllDataAsync();

            var prefs = await store.GetAllPreferencesAsync();
            var facts = await store.SearchFactsAsync("");

            Assert.Empty(prefs);
            Assert.Empty(facts);
        }
        finally
        {
            TryDeleteFile(tempDb);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static void TryDeleteFile(string path)
    {
        // Give SQLite connection pool a moment to fully release the file handle
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }
    }
}
