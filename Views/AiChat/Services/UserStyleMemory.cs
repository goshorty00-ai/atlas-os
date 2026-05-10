using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace AtlasAI.Views.AiChat.Services;

public class UserStyleMemory
{
    // 0..1
    public double PreferredSwearLevel { get; set; } = 0.2;
    public double BanterTolerance { get; set; } = 0.45;

    public void Sanitize()
    {
        PreferredSwearLevel = Math.Clamp(PreferredSwearLevel, 0, 1);
        BanterTolerance = Math.Clamp(BanterTolerance, 0, 1);
    }
}

public sealed class UserStyleMemoryStore
{
    private static readonly Lazy<UserStyleMemoryStore> LazyInstance = new(() => new UserStyleMemoryStore());
    public static UserStyleMemoryStore Instance => LazyInstance.Value;

    private readonly object _lock = new();
    private UserStyleMemory _current = new();
    private bool _loaded;

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");

    private static readonly string MemoryPath = Path.Combine(AppDataDir, "user_style_memory.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private UserStyleMemoryStore() { }

    public UserStyleMemory Current
    {
        get
        {
            EnsureLoaded();
            lock (_lock)
            {
                return new UserStyleMemory
                {
                    PreferredSwearLevel = _current.PreferredSwearLevel,
                    BanterTolerance = _current.BanterTolerance
                };
            }
        }
    }

    public void Update(Action<UserStyleMemory> updater)
    {
        EnsureLoaded();
        lock (_lock)
        {
            updater(_current);
            _current.Sanitize();
            TrySaveInternal();
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            _current = TryLoadInternal() ?? new UserStyleMemory();
            _current.Sanitize();
            _loaded = true;
        }
    }

    private static UserStyleMemory? TryLoadInternal()
    {
        try
        {
            if (!File.Exists(MemoryPath)) return null;
            var json = File.ReadAllText(MemoryPath);
            return JsonSerializer.Deserialize<UserStyleMemory>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UserStyleMemoryStore] Load failed: {ex.Message}");
            return null;
        }
    }

    private void TrySaveInternal()
    {
        try
        {
            if (!Directory.Exists(AppDataDir)) Directory.CreateDirectory(AppDataDir);
            var json = JsonSerializer.Serialize(_current, JsonOptions);
            File.WriteAllText(MemoryPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UserStyleMemoryStore] Save failed: {ex.Message}");
        }
    }
}
