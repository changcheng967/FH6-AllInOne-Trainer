using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FH6Mod.Services;

/// <summary>
/// Loads and serves localised strings from Localization/*.json.
/// Implements <see cref="INotifyPropertyChanged"/> so that XAML
/// <c>{Binding Localization[Key]}</c> bindings can receive indexer-change
/// notifications and re-evaluate when the active language switches.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly Dictionary<string, Dictionary<string, string>> _all = new();
    private Dictionary<string, string> _current = new();

    public const string FallbackCode = "en";
    private const string SubDir = "Localization";

    /// <summary>
    /// Two-letter / culture code of the active language, e.g. "en", "zh-CN", "ja".
    /// </summary>
    public string CurrentLanguage { get; private set; } = FallbackCode;

    /// <summary>
    /// All available language codes (sorted).
    /// </summary>
    public IReadOnlyList<string> AvailableLanguages { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Raised after the active language has changed. Argument is the new language code.
    /// </summary>
    public event Action<string>? LanguageChanged;

    /// <summary>
    /// Raised when the active language changes, so that <c>{Binding Localization[Key]}</c>
    /// XAML bindings re-evaluate the indexer with the new language's strings.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Scan the Localization/ directory, load every *.json, then switch to the
    /// language saved in <see cref="AppSettings.Current"/>. Safe to call multiple
    /// times (calls to SwitchTo are no-ops when the target equals CurrentLanguage).
    /// </summary>
    public void Initialize()
    {
        _all.Clear();

        var dir = ResolveDirectory();
        if (dir is null) // no Localization/ folder at all
        {
            AvailableLanguages = new[] { FallbackCode };
            _current = new Dictionary<string, string>();
            return;
        }

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var code = Path.GetFileNameWithoutExtension(file);
                var json = File.ReadAllText(file);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict is not null)
                    _all[code] = dict;
            }
            catch
            {
                // skip malformed files
            }
        }

        AvailableLanguages = _all.Keys.OrderBy(x => x).ToArray();

        // Apply persisted language; fallback to en if the saved code doesn't exist
        SwitchTo(AppSettings.Current.Language, persist: false);
    }

    /// <summary>
    /// Return the localised string for <paramref name="key"/>.
    /// Falls back to English, then to the raw key.
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (_current.TryGetValue(key, out var v)) return v;
            if (_all.TryGetValue(FallbackCode, out var en) && en.TryGetValue(key, out var fallback))
                return fallback;
            return key;
        }
    }

    /// <summary>
    /// The native display name for a language code (e.g. "English", "日本語"),
    /// extracted from its own Settings.Language key.
    /// </summary>
    public string GetDisplayName(string code)
    {
        if (_all.TryGetValue(code, out var dict) && dict.TryGetValue("Settings.Language", out var name))
            return name;
        return code;
    }

    /// <summary>
    /// Switch the active language, persist the choice, and notify subscribers.
    /// No-op when <paramref name="code"/> equals <see cref="CurrentLanguage"/>.
    /// </summary>
    public void SwitchTo(string code, bool persist = true)
    {
        if (code == CurrentLanguage) return;
        if (!_all.ContainsKey(code)) return;

        CurrentLanguage = code;
        _current = _all[code];

        if (persist)
        {
            AppSettings.Current.Language = code;
            AppSettings.Current.NotifyChanged();
        }

        // Notify subscribers of the language change
        LanguageChanged?.Invoke(code);

        // Notify XAML bindings that all indexer values on this service have changed.
        // "Item[]" is the standard .NET convention for indexer change notification;
        // Avalonia's binding engine uses it to re-evaluate {Binding Localization[Key]}.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    // ----- helpers -----

    private static string? ResolveDirectory()
    {
        // 1. AppContext.BaseDirectory (works for single-file publish when files sit next to the exe)
        var candidate = Path.Combine(AppContext.BaseDirectory, SubDir);
        if (Directory.Exists(candidate)) return candidate;

        // 2. Current directory (development / IDE runs)
        candidate = Path.Combine(Environment.CurrentDirectory, SubDir);
        if (Directory.Exists(candidate)) return candidate;

        return null;
    }
}
