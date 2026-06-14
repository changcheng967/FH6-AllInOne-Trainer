using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FH6Mod.Services;
using FH6Mod.Cheats.RuntimeHook;
using FH6Mod.Cheats.Sql;
using Material.Icons;

namespace FH6Mod.ViewModels.Pages;

public partial class AccentItemVm : ObservableObject
{
    public Accent Inner { get; }
    public string Name => Inner.Name;
    public string Base => Inner.Base;
    [ObservableProperty] private bool _isSelected;
    public AccentItemVm(Accent a, bool selected) { Inner = a; IsSelected = selected; }
}

public partial class LanguageItemVm : ObservableObject
{
    public string Code { get; }
    [ObservableProperty] private string _displayName;
    [ObservableProperty] private bool _isSelected;
    public LanguageItemVm(string code, string displayName, bool selected)
    {
        Code = code;
        _displayName = displayName;
        _isSelected = selected;
    }
}

public partial class SettingsViewModel : PageViewModelBase
{
    private readonly CheatService? _cheat;
    private readonly GameProcessService? _game;
    private readonly ProfileService? _profiles;
    private readonly LocalizationService? _localization;

    public override string PageTitle => Localization["Settings.Title"];
    public override string PageSubtitle => Localization["Settings.Subtitle"];
    public override MaterialIconKind PageIcon => MaterialIconKind.CogOutline;

    public string VersionText => $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?.?.?"}";

    [ObservableProperty] private bool _animationsEnabled;
    [ObservableProperty] private bool _mouseGlowEnabled;
    [ObservableProperty] private string _selectedAccentName = AppSettings.Current.AccentName;
    [ObservableProperty] private LanguageItemVm? _selectedLanguageItem;

    // Scan results
    [ObservableProperty] private string _scanStatus = "";
    [ObservableProperty] private bool _isScanning;

    // Conflict detection
    [ObservableProperty] private string _conflictStatus = "";
    [ObservableProperty] private bool _hasConflicts;

    // Profile management
    [ObservableProperty] private string _profileName = "";
    public ObservableCollection<string> SavedProfiles { get; } = [];
    [ObservableProperty] private string? _selectedProfile;
    [ObservableProperty] private string _profileStatus = "";

    public IReadOnlyList<AccentItemVm> AccentOptions { get; }
    public ObservableCollection<LanguageItemVm> LanguageOptions { get; } = [];

    public SettingsViewModel() : this(null, null, null, null) { }

    public SettingsViewModel(CheatService? cheat, GameProcessService? game,
                             ProfileService? profiles, LocalizationService? localization)
    {
        _cheat = cheat;
        _game = game;
        _profiles = profiles;
        _localization = localization;
        AnimationsEnabled = AppSettings.Current.AnimationsEnabled;
        MouseGlowEnabled  = AppSettings.Current.MouseGlowEnabled;
        AccentOptions = AccentPalette.All
            .Select(a => new AccentItemVm(a, a.Name == SelectedAccentName))
            .ToList();

        // Build language list
        if (localization is not null)
        {
            var current = localization.CurrentLanguage;
            foreach (var code in localization.AvailableLanguages)
            {
                var item = new LanguageItemVm(code, localization.GetDisplayName(code),
                    code == current);
                LanguageOptions.Add(item);
                if (code == current) SelectedLanguageItem = item;
            }
        }

        RefreshProfiles();
    }

    partial void OnAnimationsEnabledChanged(bool value)
    {
        AppSettings.Current.AnimationsEnabled = value;
        AppSettings.Current.NotifyChanged();
    }

    partial void OnMouseGlowEnabledChanged(bool value)
    {
        AppSettings.Current.MouseGlowEnabled = value;
        AppSettings.Current.NotifyChanged();
    }

    partial void OnSelectedLanguageItemChanged(LanguageItemVm? value)
    {
        SelectLanguage(value);
    }

    protected override void OnLanguageChanged(string code)
    {
        base.OnLanguageChanged(code);
        // Refresh display names for all language options (the native name of each
        // language may read differently in the newly-selected language).
        if (_localization is not null)
        {
            foreach (var item in LanguageOptions)
                item.DisplayName = _localization.GetDisplayName(item.Code);
        }
    }

    public void SelectLanguage(LanguageItemVm? item)
    {
        if (item is null || item.IsSelected) return;
        if (_localization is null) return;

        foreach (var x in LanguageOptions) x.IsSelected = (x == item);
        _localization.SwitchTo(item.Code);
    }

    public void SelectAccent(AccentItemVm? item)
    {
        if (item is null || item.IsSelected) return;
        foreach (var x in AccentOptions) x.IsSelected = (x == item);
        SelectedAccentName = item.Name;
        AppSettings.Current.AccentName = item.Name;
        AppSettings.Current.NotifyChanged();
        App.ApplyAccent(item.Inner);
    }

    public string SettingsPath => AppSettings.SettingsPath;

    public override IReadOnlyList<FeatureRow> Features { get; } = [];

    [RelayCommand]
    private void OpenSettingsFolder()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.SettingsDir);
            Process.Start(new ProcessStartInfo
            {
                FileName        = "explorer.exe",
                Arguments       = $"\"{AppSettings.SettingsDir}\"",
                UseShellExecute = true,
            });
        }
        catch { }
    }

    // === Signature Scan ===

    [RelayCommand]
    private void ScanSignatures()
    {
        if (_cheat is null) { ScanStatus = Localization["Settings.ServiceNotAvailable"]; return; }
        IsScanning = true;
        ScanStatus = Localization["Settings.Scanning"];

        System.Threading.Tasks.Task.Run(() =>
        {
            var results = _cheat.ScanAllSignatures();
            var found = results.Count(r => r.Found);
            var lines = results.Select(r =>
                $"{r.Feature}  —  {r.Detail}");
            return string.Format(Localization["Settings.ScanResult"], found, results.Count) + "\n\n" + string.Join("\n", lines);
        }).ContinueWith(t =>
        {
            IsScanning = false;
            ScanStatus = t.IsFaulted ? string.Format(Localization["Settings.ScanError"], t.Exception?.InnerException?.Message) : t.Result;
        });
    }

    // === Conflict Detection ===

    [RelayCommand]
    private void DetectConflicts()
    {
        if (_game is null) { ConflictStatus = Localization["Settings.ServiceNotAvailable"]; return; }
        var conflicts = _game.DetectConflictingTrainers();
        HasConflicts = conflicts.Count > 0;
        ConflictStatus = conflicts.Count > 0
            ? string.Format(Localization["Settings.ConflictWarning"], string.Join("\n", conflicts))
            : Localization["Settings.NoConflicts"];
    }

    // === Profile Management ===

    [RelayCommand]
    private void SaveProfile()
    {
        if (_profiles is null) { ProfileStatus = Localization["Settings.ServiceNotAvailable"]; return; }
        var name = ProfileName.Trim();
        if (string.IsNullOrWhiteSpace(name)) { ProfileStatus = Localization["Settings.EnterProfileName"]; return; }

        var profile = new CheatProfile();
        // Capture current cheat states from CheatService if available
        if (_cheat is not null)
        {
            foreach (RuntimeProfileFeature f in Enum.GetValues<RuntimeProfileFeature>())
            {
                if (_cheat.IsActive(f))
                    profile.Features[f] = new CheatState { Enabled = true };
            }
            foreach (SqlFeature f in Enum.GetValues<SqlFeature>())
            {
                if (_cheat.IsSqlLockActive(f))
                    profile.SqlLocks[f] = true;
            }
        }

        _profiles.Save(name, profile);
        ProfileStatus = string.Format(Localization["Settings.SavedLabel"], name);
        ProfileName = "";
        RefreshProfiles();
    }

    [RelayCommand]
    private void LoadProfile()
    {
        if (_profiles is null || SelectedProfile is null) { ProfileStatus = Localization["Settings.SelectProfileFirst"]; return; }
        var profile = _profiles.Load(SelectedProfile);
        if (profile is null) { ProfileStatus = Localization["Settings.LoadFailed"]; return; }
        ProfileStatus = string.Format(Localization["Settings.LoadedLabel"], SelectedProfile);
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (_profiles is null || SelectedProfile is null) { ProfileStatus = Localization["Settings.SelectProfileFirst"]; return; }
        _profiles.Delete(SelectedProfile);
        ProfileStatus = string.Format(Localization["Settings.DeletedLabel"], SelectedProfile);
        RefreshProfiles();
    }

    [RelayCommand]
    private void OpenProfilesFolder()
    {
        try
        {
            Directory.CreateDirectory(ProfileService.ProfilesDir);
            Process.Start(new ProcessStartInfo
            {
                FileName  = "explorer.exe",
                Arguments = $"\"{ProfileService.ProfilesDir}\"",
                UseShellExecute = true,
            });
        }
        catch { }
    }

    private void RefreshProfiles()
    {
        SavedProfiles.Clear();
        if (_profiles is null) return;
        foreach (var p in _profiles.ListProfiles())
            SavedProfiles.Add(p);
    }
}
