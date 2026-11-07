using System.IO;
using System.Text.Json;
using FlowAI.Core.Configuration;
using FlowAI.Core.Interfaces;
using FlowAI.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace FlowAI.Memory;

public sealed class SqliteMemoryStore : IMemoryStore
{
    private readonly ILogger<SqliteMemoryStore>? logger;
    private readonly string connectionString;
    private bool isInitialized;
    private readonly SemaphoreSlim dbLock = new(1, 1);

    public static string DatabaseFilePath =>
        Path.Combine(FlowAISettings.AppDataDirectory, "flowai_memory.db");

    public SqliteMemoryStore(string? dbPath = null, ILogger<SqliteMemoryStore>? logger = null)
    {
        this.logger = logger;
        string path = dbPath ?? DatabaseFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        connectionString = $"Data Source={path}";
    }

    public async Task InitializeAsync()
    {
        if (isInitialized) return;

        await dbLock.WaitAsync();
        try
        {
            if (isInitialized) return;

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            string initSql = @"
                CREATE TABLE IF NOT EXISTS UserPreferences (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL,
                    category TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS LearnedFacts (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    fact TEXT NOT NULL,
                    context TEXT NOT NULL,
                    confidence REAL NOT NULL,
                    created_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Corrections (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    app_name TEXT NOT NULL,
                    window_title TEXT NOT NULL,
                    user_query TEXT NOT NULL,
                    wrong_target TEXT NOT NULL,
                    corrected_target TEXT NOT NULL,
                    selector_json TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_corrections_app ON Corrections(app_name);

                CREATE TABLE IF NOT EXISTS Workflows (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT UNIQUE NOT NULL,
                    description TEXT NOT NULL,
                    target_app TEXT NOT NULL,
                    preconditions TEXT NOT NULL,
                    steps_json TEXT NOT NULL,
                    expected_result TEXT NOT NULL,
                    version INTEGER NOT NULL DEFAULT 1,
                    is_approved INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL,
                    last_used_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_workflows_name ON Workflows(name);
                CREATE INDEX IF NOT EXISTS idx_workflows_app ON Workflows(target_app);

                CREATE TABLE IF NOT EXISTS TaskHistory (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    task_query TEXT NOT NULL,
                    app_name TEXT NOT NULL,
                    is_success INTEGER NOT NULL,
                    model_used TEXT NOT NULL,
                    duration_ms REAL NOT NULL,
                    feedback TEXT,
                    created_at TEXT NOT NULL
                );
            ";

            using var command = new SqliteCommand(initSql, connection);
            await command.ExecuteNonQueryAsync();

            isInitialized = true;
            logger?.LogInformation("SQLite memory store initialized successfully");
        }
        finally
        {
            dbLock.Release();
        }
    }

    public async Task SavePreferenceAsync(UserPreference preference)
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        string sql = @"
            INSERT INTO UserPreferences (key, value, category, updated_at)
            VALUES (@key, @val, @cat, @upd)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                category = excluded.category,
                updated_at = excluded.updated_at;
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@key", preference.Key);
        cmd.Parameters.AddWithValue("@val", preference.Value);
        cmd.Parameters.AddWithValue("@cat", preference.Category);
        cmd.Parameters.AddWithValue("@upd", preference.UpdatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetPreferenceAsync(string key)
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        string sql = "SELECT value FROM UserPreferences WHERE key = @key LIMIT 1;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@key", key);

        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString();
    }

    public async Task<IReadOnlyList<UserPreference>> GetAllPreferencesAsync()
    {
        await InitializeAsync();
        var list = new List<UserPreference>();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        string sql = "SELECT key, value, category, updated_at FROM UserPreferences ORDER BY category, key;";
        using var cmd = new SqliteCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new UserPreference
            {
                Key = reader.GetString(0),
                Value = reader.GetString(1),
                Category = reader.GetString(2),
                UpdatedAt = DateTime.TryParse(reader.GetString(3), out var dt) ? dt : DateTime.UtcNow
            });
        }
        return list;
    }

    public async Task DeletePreferenceAsync(string key)
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = new SqliteCommand("DELETE FROM UserPreferences WHERE key = @k;", conn);
        cmd.Parameters.AddWithValue("@k", key);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveFactAsync(LearnedFact fact)
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        string sql = "INSERT INTO LearnedFacts (fact, context, confidence, created_at) VALUES (@f, @ctx, @c, @dt);";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@f", fact.Fact);
        cmd.Parameters.AddWithValue("@ctx", fact.Context);
        cmd.Parameters.AddWithValue("@c", fact.Confidence);
        cmd.Parameters.AddWithValue("@dt", fact.CreatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<LearnedFact>> SearchFactsAsync(string query)
    {
        await InitializeAsync();
        var list = new List<LearnedFact>();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        string sql = "SELECT id, fact, context, confidence, created_at FROM LearnedFacts WHERE fact LIKE @q OR context LIKE @q ORDER BY id DESC;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@q", $"%{query}%");
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new LearnedFact
            {
                Id = reader.GetInt64(0),
                Fact = reader.GetString(1),
                Context = reader.GetString(2),
                Confidence = reader.GetDouble(3),
                CreatedAt = DateTime.TryParse(reader.GetString(4), out var dt) ? dt : DateTime.UtcNow
            });
        }
        return list;
    }

    public async Task DeleteFactAsync(long id)
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = new SqliteCommand("DELETE FROM LearnedFacts WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveCorrectionAsync(CorrectionRecord correction)
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        string sql = @"
            INSERT INTO Corrections (app_name, window_title, user_query, wrong_target, corrected_target, selector_json, created_at)
            VALUES (@app, @title, @query, @wrong, @correct, @sel, @dt);
        ";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@app", correction.ApplicationName);
        cmd.Parameters.AddWithValue("@title", correction.WindowTitle);
        cmd.Parameters.AddWithValue("@query", correction.UserQuery);
        cmd.Parameters.AddWithValue("@wrong", correction.WrongTargetLabel);
        cmd.Parameters.AddWithValue("@correct", correction.CorrectedTargetLabel);
        cmd.Parameters.AddWithValue("@sel", correction.ElementSelectorJson);
        cmd.Parameters.AddWithValue("@dt", correction.CreatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<CorrectionRecord>> GetCorrectionsForAppAsync(string appName)
    {
        await InitializeAsync();
        var list = new List<CorrectionRecord>();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        string sql = "SELECT id, app_name, window_title, user_query, wrong_target, corrected_target, selector_json, created_at FROM Corrections WHERE app_name = @app ORDER BY id DESC;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@app", appName);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new CorrectionRecord
            {
                Id = reader.GetInt64(0),
                ApplicationName = reader.GetString(1),
                WindowTitle = reader.GetString(2),
                UserQuery = reader.GetString(3),
                WrongTargetLabel = reader.GetString(4),
                CorrectedTargetLabel = reader.GetString(5),
                ElementSelectorJson = reader.GetString(6),
                CreatedAt = DateTime.TryParse(reader.GetString(7), out var dt) ? dt : DateTime.UtcNow
            });
        }
        return list;
    }

    public async Task<IReadOnlyList<CorrectionRecord>> GetAllCorrectionsAsync()
    {
        await InitializeAsync();
        var list = new List<CorrectionRecord>();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        string sql = "SELECT id, app_name, window_title, user_query, wrong_target, corrected_target, selector_json, created_at FROM Corrections ORDER BY id DESC;";
        using var cmd = new SqliteCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new CorrectionRecord
            {
                Id = reader.GetInt64(0),
                ApplicationName = reader.GetString(1),
                WindowTitle = reader.GetString(2),
                UserQuery = reader.GetString(3),
                WrongTargetLabel = reader.GetString(4),
                CorrectedTargetLabel = reader.GetString(5),
                ElementSelectorJson = reader.GetString(6),
                CreatedAt = DateTime.TryParse(reader.GetString(7), out var dt) ? dt : DateTime.UtcNow
            });
        }
        return list;
    }

    public async Task DeleteCorrectionAsync(long id)
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = new SqliteCommand("DELETE FROM Corrections WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<long> SaveWorkflowAsync(LearnedWorkflow workflow)
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        string stepsJson = JsonSerializer.Serialize(workflow.Steps);
        string sql = @"
            INSERT INTO Workflows (name, description, target_app, preconditions, steps_json, expected_result, version, is_approved, created_at, last_used_at)
            VALUES (@name, @desc, @app, @pre, @steps, @exp, @ver, @appr, @created, @used)
            ON CONFLICT(name) DO UPDATE SET
                description = excluded.description,
                target_app = excluded.target_app,
                preconditions = excluded.preconditions,
                steps_json = excluded.steps_json,
                expected_result = excluded.expected_result,
                version = Workflows.version + 1,
                last_used_at = excluded.last_used_at;
            SELECT last_insert_rowid();
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@name", workflow.Name);
        cmd.Parameters.AddWithValue("@desc", workflow.Description);
        cmd.Parameters.AddWithValue("@app", workflow.TargetApplication);
        cmd.Parameters.AddWithValue("@pre", workflow.Preconditions);
        cmd.Parameters.AddWithValue("@steps", stepsJson);
        cmd.Parameters.AddWithValue("@exp", workflow.ExpectedResult);
        cmd.Parameters.AddWithValue("@ver", workflow.Version);
        cmd.Parameters.AddWithValue("@appr", workflow.IsApproved ? 1 : 0);
        cmd.Parameters.AddWithValue("@created", workflow.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@used", workflow.LastUsedAt.ToString("o"));

        var rowId = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(rowId);
    }

    public async Task<IReadOnlyList<LearnedWorkflow>> GetWorkflowsAsync()
    {
        await InitializeAsync();
        var list = new List<LearnedWorkflow>();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        string sql = "SELECT id, name, description, target_app, preconditions, steps_json, expected_result, version, is_approved, created_at, last_used_at FROM Workflows ORDER BY last_used_at DESC;";
        using var cmd = new SqliteCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var steps = new List<WorkflowStep>();
            try
            {
                steps = JsonSerializer.Deserialize<List<WorkflowStep>>(reader.GetString(5)) ?? new List<WorkflowStep>();
            }
            catch { }

            list.Add(new LearnedWorkflow
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                TargetApplication = reader.GetString(3),
                Preconditions = reader.GetString(4),
                Steps = steps,
                ExpectedResult = reader.GetString(6),
                Version = reader.GetInt32(7),
                IsApproved = reader.GetInt32(8) == 1,
                CreatedAt = DateTime.TryParse(reader.GetString(9), out var dt) ? dt : DateTime.UtcNow,
                LastUsedAt = DateTime.TryParse(reader.GetString(10), out var dt2) ? dt2 : DateTime.UtcNow
            });
        }
        return list;
    }

    public async Task<LearnedWorkflow?> GetWorkflowByNameAsync(string name)
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        string sql = "SELECT id, name, description, target_app, preconditions, steps_json, expected_result, version, is_approved, created_at, last_used_at FROM Workflows WHERE name = @name LIMIT 1;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@name", name);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var steps = new List<WorkflowStep>();
            try
            {
                steps = JsonSerializer.Deserialize<List<WorkflowStep>>(reader.GetString(5)) ?? new List<WorkflowStep>();
            }
            catch { }

            return new LearnedWorkflow
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                TargetApplication = reader.GetString(3),
                Preconditions = reader.GetString(4),
                Steps = steps,
                ExpectedResult = reader.GetString(6),
                Version = reader.GetInt32(7),
                IsApproved = reader.GetInt32(8) == 1,
                CreatedAt = DateTime.TryParse(reader.GetString(9), out var dt) ? dt : DateTime.UtcNow,
                LastUsedAt = DateTime.TryParse(reader.GetString(10), out var dt2) ? dt2 : DateTime.UtcNow
            };
        }
        return null;
    }

    public async Task DeleteWorkflowAsync(long id)
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        string sql = "DELETE FROM Workflows WHERE id = @id;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RecordTaskHistoryAsync(TaskHistoryRecord record)
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        string sql = @"
            INSERT INTO TaskHistory (task_query, app_name, is_success, model_used, duration_ms, feedback, created_at)
            VALUES (@query, @app, @success, @model, @dur, @fb, @dt);
        ";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@query", record.TaskQuery);
        cmd.Parameters.AddWithValue("@app", record.ApplicationName);
        cmd.Parameters.AddWithValue("@success", record.IsSuccess ? 1 : 0);
        cmd.Parameters.AddWithValue("@model", record.ModelUsed);
        cmd.Parameters.AddWithValue("@dur", record.DurationMilliseconds);
        cmd.Parameters.AddWithValue("@fb", (object?)record.Feedback ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dt", record.Timestamp.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<TaskHistoryRecord>> GetRecentTaskHistoryAsync(int limit = 50)
    {
        await InitializeAsync();
        var list = new List<TaskHistoryRecord>();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        string sql = "SELECT id, task_query, app_name, is_success, model_used, duration_ms, feedback, created_at FROM TaskHistory ORDER BY id DESC LIMIT @limit;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new TaskHistoryRecord
            {
                Id = reader.GetInt64(0),
                TaskQuery = reader.GetString(1),
                ApplicationName = reader.GetString(2),
                IsSuccess = reader.GetInt32(3) == 1,
                ModelUsed = reader.GetString(4),
                DurationMilliseconds = reader.GetDouble(5),
                Feedback = reader.IsDBNull(6) ? null : reader.GetString(6),
                Timestamp = DateTime.TryParse(reader.GetString(7), out var dt) ? dt : DateTime.UtcNow
            });
        }
        return list;
    }

    public async Task ClearAllDataAsync()
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        string sql = @"
            DELETE FROM UserPreferences;
            DELETE FROM LearnedFacts;
            DELETE FROM Corrections;
            DELETE FROM Workflows;
            DELETE FROM TaskHistory;
        ";
        using var cmd = new SqliteCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
        logger?.LogInformation("All memory store data cleared");
    }

    public void Dispose()
    {
        // Clear SQLite connection pool to release file lock promptly.
        // Important for tests that need to delete the DB file after use.
        try { SqliteConnection.ClearAllPools(); } catch { }
        dbLock.Dispose();
    }
}
