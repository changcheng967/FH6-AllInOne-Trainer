using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FH6Mod.Services;

/// <summary>
/// Persistent preferences in %APPDATA%\FH6AllInOneTrainer\settings.json.
///
/// Read/write goes through System.Text.Json's DOM (JsonDocument + JsonObject) —
/// NOT JsonSerializer.Deserialize&lt;T&gt;/Serialize&lt;T&gt;. The serializer's
/// reflection path can silently get trimmed out of a single-file self-contained
/// .NET 10 publish, which gives a "loads but returns defaults" failure mode. DOM
/// parsing is trim-safe by construction.
/// </summary>
public sealed class AppSettings
{
    public bool   AnimationsEnabled    { get; set; } = true;
    public int    AnimationStaggerMs   { get; set; } = 60;
    public int    AnimationDurationMs  { get; set; } = 320;
    public string AccentName           { get; set; } = AccentPalette.DefaultName;
    public bool   MouseGlowEnabled     { get; set; } = true;
    public string Language             { get; set; } = "en";

    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FH6AllInOneTrainer",
        "settings.json");

    public static string SettingsDir  => Path.GetDirectoryName(FilePath)!;
    public static string SettingsPath => FilePath;

    public static AppSettings Current { get; } = Load();

    private static AppSettings Load()
    {
        var s = new AppSettings();
        try
        {
            if (!File.Exists(FilePath)) return s;
            var json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json)) return s;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return s;

            if (root.TryGetProperty("AnimationsEnabled", out var a) &&
                (a.ValueKind == JsonValueKind.True || a.ValueKind == JsonValueKind.False))
                s.AnimationsEnabled = a.GetBoolean();

            if (root.TryGetProperty("AnimationStaggerMs", out var b) && b.ValueKind == JsonValueKind.Number)
                s.AnimationStaggerMs = b.GetInt32();

            if (root.TryGetProperty("AnimationDurationMs", out var c) && c.ValueKind == JsonValueKind.Number)
                s.AnimationDurationMs = c.GetInt32();

            if (root.TryGetProperty("AccentName", out var d) && d.ValueKind == JsonValueKind.String)
            {
                var name = d.GetString();
                if (!string.IsNullOrWhiteSpace(name)) s.AccentName = name;
            }

            if (root.TryGetProperty("MouseGlowEnabled", out var e) &&
                (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False))
                s.MouseGlowEnabled = e.GetBoolean();

            if (root.TryGetProperty("Language", out var lang) && lang.ValueKind == JsonValueKind.String)
            {
                var l = lang.GetString();
                if (!string.IsNullOrWhiteSpace(l)) s.Language = l;
            }
        }
        catch
        {
            // malformed JSON → keep whatever we already populated; remaining fields keep defaults
        }
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var node = new JsonObject
            {
                ["AnimationsEnabled"]    = AnimationsEnabled,
                ["AnimationStaggerMs"]   = AnimationStaggerMs,
                ["AnimationDurationMs"]  = AnimationDurationMs,
                ["AccentName"]           = AccentName,
                ["MouseGlowEnabled"]     = MouseGlowEnabled,
                ["Language"]              = Language,
            };
            File.WriteAllText(FilePath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // disk full / locked → silent
        }
    }

    public event Action? Changed;
    public void NotifyChanged() { Changed?.Invoke(); Save(); }
}
