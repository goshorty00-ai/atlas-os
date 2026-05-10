using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace AtlasAI.Memory
{
    /// <summary>
    /// Long-term memory storage for Atlas - persists across sessions
    /// Stores user preferences, corrections, learned patterns, and facts
    /// </summary>
    public class LongTermMemoryStore : IDisposable
    {
        private static readonly string DbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "atlas_memory.db");
        
        private SqliteConnection? _connection;
        private static LongTermMemoryStore? _instance;
        private static readonly object _lock = new();

        public static LongTermMemoryStore Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new LongTermMemoryStore();
                    }
                }
                return _instance;
            }
        }

        private LongTermMemoryStore()
        {
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            try
            {
                var dir = Path.GetDirectoryName(DbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                _connection = new SqliteConnection($"Data Source={DbPath}");
                _connection.Open();

                // Create tables
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    -- User preferences (e.g., 'preferred_image_tool' = 'Photoshop')
                    CREATE TABLE IF NOT EXISTS preferences (
                        key TEXT PRIMARY KEY,
                        value TEXT NOT NULL,
                        category TEXT DEFAULT 'general',
                        created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                        updated_at TEXT DEFAULT CURRENT_TIMESTAMP,
                        source TEXT DEFAULT 'user'
                    );

                    -- Corrections the user made (e.g., 'Don't use Canva')
                    CREATE TABLE IF NOT EXISTS corrections (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        original_action TEXT NOT NULL,
                        corrected_action TEXT NOT NULL,
                        context TEXT,
                        times_applied INTEGER DEFAULT 1,
                        created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                        last_applied TEXT DEFAULT CURRENT_TIMESTAMP
                    );

                    -- Facts Atlas has learned about the user
                    CREATE TABLE IF NOT EXISTS user_facts (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        fact TEXT NOT NULL,
                        category TEXT DEFAULT 'general',
                        confidence REAL DEFAULT 1.0,
                        source TEXT DEFAULT 'conversation',
                        created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                        last_referenced TEXT DEFAULT CURRENT_TIMESTAMP
                    );

                    -- Skill usage patterns
                    CREATE TABLE IF NOT EXISTS skill_usage (
                        skill_name TEXT PRIMARY KEY,
                        use_count INTEGER DEFAULT 1,
                        success_count INTEGER DEFAULT 0,
                        failure_count INTEGER DEFAULT 0,
                        avg_duration_ms INTEGER DEFAULT 0,
                        last_used TEXT DEFAULT CURRENT_TIMESTAMP,
                        user_rating REAL DEFAULT 0
                    );

                    -- Conversation summaries for context
                    CREATE TABLE IF NOT EXISTS conversation_summaries (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        summary TEXT NOT NULL,
                        key_topics TEXT,
                        created_at TEXT DEFAULT CURRENT_TIMESTAMP
                    );

                    -- Create indexes
                    CREATE INDEX IF NOT EXISTS idx_preferences_category ON preferences(category);
                    CREATE INDEX IF NOT EXISTS idx_corrections_original ON corrections(original_action);
                    CREATE INDEX IF NOT EXISTS idx_user_facts_category ON user_facts(category);
                ";
                cmd.ExecuteNonQuery();

                System.Diagnostics.Debug.WriteLine("[Memory] Long-term memory database initialized");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Memory] Database init error: {ex.Message}");
            }
        }

        #region Preferences

        /// <summary>
        /// Store a user preference
        /// </summary>
        public async Task SetPreferenceAsync(string key, string value, string category = "general", string source = "user")
        {
            if (_connection == null) return;

            await Task.Run(() =>
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO preferences (key, value, category, source, updated_at)
                        VALUES (@key, @value, @category, @source, CURRENT_TIMESTAMP)
                        ON CONFLICT(key) DO UPDATE SET 
                            value = @value, 
                            category = @category,
                            updated_at = CURRENT_TIMESTAMP";
                    cmd.Parameters.AddWithValue("@key", key);
                    cmd.Parameters.AddWithValue("@value", value);
                    cmd.Parameters.AddWithValue("@category", category);
                    cmd.Parameters.AddWithValue("@source", source);
                    cmd.ExecuteNonQuery();

                    System.Diagnostics.Debug.WriteLine($"[Memory] Preference saved: {key} = {value}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Memory] Error saving preference: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Get a user preference
        /// </summary>
        public async Task<string?> GetPreferenceAsync(string key)
        {
            if (_connection == null) return null;

            return await Task.Run(() =>
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "SELECT value FROM preferences WHERE key = @key";
                    cmd.Parameters.AddWithValue("@key", key);
                    return cmd.ExecuteScalar()?.ToString();
                }
                catch
                {
                    return null;
                }
            });
        }

        /// <summary>
        /// Get all preferences in a category
        /// </summary>
        public async Task<Dictionary<string, string>> GetPreferencesByCategoryAsync(string category)
        {
            var result = new Dictionary<string, string>();
            if (_connection == null) return result;

            await Task.Run(() =>
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "SELECT key, value FROM preferences WHERE category = @category";
                    cmd.Parameters.AddWithValue("@category", category);
                    
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        result[reader.GetString(0)] = reader.GetString(1);
                    }
                }
                catch { }
            });

            return result;
        }

        #endregion

        #region Corrections

        /// <summary>
        /// Record a correction the user made
        /// </summary>
        public async Task RecordCorrectionAsync(string originalAction, string correctedAction, string? context = null)
        {
            if (_connection == null) return;

            await Task.Run(() =>
            {
                try
                {
                    // Check if similar correction exists
                    using var checkCmd = _connection.CreateCommand();
                    checkCmd.CommandText = @"
                        SELECT id, times_applied FROM corrections 
                        WHERE original_action = @original AND corrected_action = @corrected";
                    checkCmd.Parameters.AddWithValue("@original", originalAction);
                    checkCmd.Parameters.AddWithValue("@corrected", correctedAction);
                    
                    using var reader = checkCmd.ExecuteReader();
                    if (reader.Read())
                    {
                        // Update existing
                        var id = reader.GetInt32(0);
                        var times = reader.GetInt32(1);
                        reader.Close();

                        using var updateCmd = _connection.CreateCommand();
                        updateCmd.CommandText = @"
                            UPDATE corrections SET 
                                times_applied = @times,
                                last_applied = CURRENT_TIMESTAMP
                            WHERE id = @id";
                        updateCmd.Parameters.AddWithValue("@times", times + 1);
                        updateCmd.Parameters.AddWithValue("@id", id);
                        updateCmd.ExecuteNonQuery();
                    }
                    else
                    {
                        reader.Close();
                        // Insert new
                        using var insertCmd = _connection.CreateCommand();
                        insertCmd.CommandText = @"
                            INSERT INTO corrections (original_action, corrected_action, context)
                            VALUES (@original, @corrected, @context)";
                        insertCmd.Parameters.AddWithValue("@original", originalAction);
                        insertCmd.Parameters.AddWithValue("@corrected", correctedAction);
                        insertCmd.Parameters.AddWithValue("@context", context ?? "");
                        insertCmd.ExecuteNonQuery();
                    }

                    System.Diagnostics.Debug.WriteLine($"[Memory] Correction recorded: '{originalAction}' → '{correctedAction}'");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Memory] Error recording correction: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Check if there's a correction for an action
        /// </summary>
        public async Task<string?> GetCorrectionForAsync(string originalAction)
        {
            if (_connection == null) return null;

            return await Task.Run(() =>
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"
                        SELECT corrected_action FROM corrections 
                        WHERE original_action LIKE @original
                        ORDER BY times_applied DESC, last_applied DESC
                        LIMIT 1";
                    cmd.Parameters.AddWithValue("@original", $"%{originalAction}%");
                    return cmd.ExecuteScalar()?.ToString();
                }
                catch
                {
                    return null;
                }
            });
        }

        /// <summary>
        /// Get all corrections (for learning context)
        /// </summary>
        public async Task<List<(string Original, string Corrected, int TimesApplied)>> GetAllCorrectionsAsync()
        {
            var result = new List<(string, string, int)>();
            if (_connection == null) return result;

            await Task.Run(() =>
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"
                        SELECT original_action, corrected_action, times_applied 
                        FROM corrections 
                        ORDER BY times_applied DESC";
                    
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        result.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
                    }
                }
                catch { }
            });

            return result;
        }

        #endregion

        #region User Facts

        /// <summary>
        /// Store a fact about the user
        /// </summary>
        public async Task LearnFactAsync(string fact, string category = "general", double confidence = 1.0, string source = "conversation")
        {
            if (_connection == null) return;

            await Task.Run(() =>
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO user_facts (fact, category, confidence, source)
                        VALUES (@fact, @category, @confidence, @source)";
                    cmd.Parameters.AddWithValue("@fact", fact);
                    cmd.Parameters.AddWithValue("@category", category);
                    cmd.Parameters.AddWithValue("@confidence", confidence);
                    cmd.Parameters.AddWithValue("@source", source);
                    cmd.ExecuteNonQuery();

                    System.Diagnostics.Debug.WriteLine($"[Memory] Learned fact: {fact}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Memory] Error learning fact: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Get facts about the user
        /// </summary>
        public async Task<List<string>> GetFactsAsync(string? category = null, int limit = 20)
        {
            var result = new List<string>();
            if (_connection == null) return result;

            await Task.Run(() =>
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = category != null
                        ? "SELECT fact FROM user_facts WHERE category = @category ORDER BY confidence DESC, last_referenced DESC LIMIT @limit"
                        : "SELECT fact FROM user_facts ORDER BY confidence DESC, last_referenced DESC LIMIT @limit";
                    
                    if (category != null)
                        cmd.Parameters.AddWithValue("@category", category);
                    cmd.Parameters.AddWithValue("@limit", limit);
                    
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        result.Add(reader.GetString(0));
                    }
                }
                catch { }
            });

            return result;
        }

        #endregion

        #region Skill Usage

        /// <summary>
        /// Track skill/tool usage
        /// </summary>
        public async Task TrackSkillUsageAsync(string skillName, bool success, int durationMs = 0)
        {
            if (_connection == null) return;

            await Task.Run(() =>
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO skill_usage (skill_name, use_count, success_count, failure_count, avg_duration_ms, last_used)
                        VALUES (@name, 1, @success, @failure, @duration, CURRENT_TIMESTAMP)
                        ON CONFLICT(skill_name) DO UPDATE SET 
                            use_count = use_count + 1,
                            success_count = success_count + @success,
                            failure_count = failure_count + @failure,
                            avg_duration_ms = (avg_duration_ms + @duration) / 2,
                            last_used = CURRENT_TIMESTAMP";
                    cmd.Parameters.AddWithValue("@name", skillName);
                    cmd.Parameters.AddWithValue("@success", success ? 1 : 0);
                    cmd.Parameters.AddWithValue("@failure", success ? 0 : 1);
                    cmd.Parameters.AddWithValue("@duration", durationMs);
                    cmd.ExecuteNonQuery();
                }
                catch { }
            });
        }

        /// <summary>
        /// Get skill usage stats
        /// </summary>
        public async Task<Dictionary<string, (int Uses, int Successes, int Failures)>> GetSkillStatsAsync()
        {
            var result = new Dictionary<string, (int, int, int)>();
            if (_connection == null) return result;

            await Task.Run(() =>
            {
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "SELECT skill_name, use_count, success_count, failure_count FROM skill_usage ORDER BY use_count DESC";
                    
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        result[reader.GetString(0)] = (reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
                    }
                }
                catch { }
            });

            return result;
        }

        #endregion

        #region Context Building

        /// <summary>
        /// Build a memory context string for AI prompts
        /// </summary>
        public async Task<string> BuildMemoryContextAsync()
        {
            var context = new System.Text.StringBuilder();
            
            // Get preferences
            var prefs = await GetPreferencesByCategoryAsync("tools");
            if (prefs.Count > 0)
            {
                context.AppendLine("## User Tool Preferences:");
                foreach (var pref in prefs)
                {
                    context.AppendLine($"- {pref.Key}: {pref.Value}");
                }
                context.AppendLine();
            }

            // Get corrections
            var corrections = await GetAllCorrectionsAsync();
            if (corrections.Count > 0)
            {
                context.AppendLine("## User Corrections (IMPORTANT - follow these):");
                foreach (var (original, corrected, times) in corrections.Take(10))
                {
                    context.AppendLine($"- Instead of '{original}', use '{corrected}' (corrected {times}x)");
                }
                context.AppendLine();
            }

            // Get facts
            var facts = await GetFactsAsync(limit: 10);
            if (facts.Count > 0)
            {
                context.AppendLine("## Known Facts About User:");
                foreach (var fact in facts)
                {
                    context.AppendLine($"- {fact}");
                }
                context.AppendLine();
            }

            return context.ToString();
        }

        #endregion

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
        }
    }
}
