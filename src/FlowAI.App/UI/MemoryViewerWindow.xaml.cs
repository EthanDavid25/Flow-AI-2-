using System.Windows;
using FlowAI.Core.Interfaces;
using FlowAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace FlowAI.App.UI;

public partial class MemoryViewerWindow : Window
{
    private readonly IMemoryStore _memoryStore;
    private readonly ILogger<MemoryViewerWindow>? _logger;

    public MemoryViewerWindow(IMemoryStore memoryStore, ILogger<MemoryViewerWindow>? logger = null)
    {
        _memoryStore = memoryStore;
        _logger = logger;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAllDataAsync();
    }

    private async Task LoadAllDataAsync()
    {
        try
        {
            var workflows = await _memoryStore.GetWorkflowsAsync();
            WorkflowsGrid.ItemsSource = workflows;

            var corrections = await _memoryStore.GetAllCorrectionsAsync();
            CorrectionsGrid.ItemsSource = corrections;

            var preferences = await _memoryStore.GetAllPreferencesAsync();
            PreferencesGrid.ItemsSource = preferences;

            var history = await _memoryStore.GetRecentTaskHistoryAsync(100);
            HistoryGrid.ItemsSource = history;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load memory store data");
            MessageBox.Show($"Failed to load data: {ex.Message}", "Memory Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadAllDataAsync();
    }

    private async void DeleteWorkflow_Click(object sender, RoutedEventArgs e)
    {
        if (WorkflowsGrid.SelectedItem is LearnedWorkflow wf)
        {
            var confirm = MessageBox.Show($"Delete workflow '{wf.Name}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                await _memoryStore.DeleteWorkflowAsync(wf.Id);
                await LoadAllDataAsync();
            }
        }
        else
        {
            MessageBox.Show("Please select a workflow to delete.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void DeleteCorrection_Click(object sender, RoutedEventArgs e)
    {
        if (CorrectionsGrid.SelectedItem is CorrectionRecord corr)
        {
            var confirm = MessageBox.Show($"Delete correction for query '{corr.UserQuery}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                await _memoryStore.DeleteCorrectionAsync(corr.Id);
                await LoadAllDataAsync();
            }
        }
        else
        {
            MessageBox.Show("Please select a correction to delete.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void DeletePreference_Click(object sender, RoutedEventArgs e)
    {
        if (PreferencesGrid.SelectedItem is UserPreference pref)
        {
            var confirm = MessageBox.Show($"Delete preference '{pref.Key}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                await _memoryStore.DeletePreferenceAsync(pref.Key);
                await LoadAllDataAsync();
            }
        }
        else
        {
            MessageBox.Show("Please select a preference to delete.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Are you sure you want to delete ALL FlowAI memories, workflows, corrections, and history?\n\nThis cannot be undone.",
            "Confirm Clear All Memory",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm == MessageBoxResult.Yes)
        {
            try
            {
                await _memoryStore.ClearAllDataAsync();
                await LoadAllDataAsync();
                MessageBox.Show("All memory store data has been cleared.", "Memory Cleared", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to clear data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
