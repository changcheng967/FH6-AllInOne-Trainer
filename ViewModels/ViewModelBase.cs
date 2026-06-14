using System;
using CommunityToolkit.Mvvm.ComponentModel;
using FH6Mod.Services;

namespace FH6Mod.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Exposes the app-wide localisation service for direct use in code-behind.
    /// For AXAML bindings, use the indexer <c>{Binding [Key]}</c> instead —
    /// it raises <see cref="INotifyPropertyChanged.PropertyChanged"/> with
    /// <c>"Item"</c> when the language switches, so bindings re-evaluate correctly
    /// without relying on nested path notifications from Avalonia.
    /// </summary>
    public LocalizationService Localization => App.Localization;

    /// <summary>
    /// Indexer for AXAML: <c>{Binding [Unlocks.QuickActions]}</c>
    /// Because the indexer lives on the DataContext itself, raising
    /// <c>PropertyChanged("Item")</c> causes Avalonia to re-evaluate the binding
    /// immediately when the language changes.
    /// </summary>
    public string? this[string key] => Localization[key];

    protected ViewModelBase()
    {
        App.Localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Called when the active language changes. Raises <see cref="ObservableObject.PropertyChanged"/>
    /// with <c>"Item"</c> — the standard .NET convention for notifying that all
    /// indexer values on this object have changed.  This causes every
    /// <c>{Binding [Key]}</c> binding on this ViewModel to re-evaluate.
    /// </summary>
    protected virtual void OnLanguageChanged(string code)
    {
        OnPropertyChanged("Item");
    }
}
