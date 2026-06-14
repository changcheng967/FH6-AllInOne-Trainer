using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using FH6Mod.Services;

namespace FH6Mod.Localization;

/// <summary>
/// Avalonia markup extension that creates a one-way binding directly to
/// <see cref="App.Localization"/> with the indexer path <c>[Key]</c>.
///
/// Because the binding source IS the <see cref="LocalizationService"/> itself
/// (which implements <see cref="System.ComponentModel.INotifyPropertyChanged"/>
/// and raises <c>PropertyChanged("Item[]")</c> when the language switches),
/// the binding re-evaluates automatically at runtime — no app restart needed.
///
/// Usage in AXAML:
/// <code>
/// xmlns:local="using:FH6Mod.Localization"
/// ...
/// &lt;TextBlock Text="{local:Loc Unlocks.QuickActions}" /&gt;
/// &lt;TextBlock Text="{local:Loc App.Title, FallbackValue='FH6 Trainer'}" /&gt;
/// </code>
/// </summary>
public class LocExtension : MarkupExtension
{
    public string Key { get; }
    public object? FallbackValue { get; set; }
    public object? TargetNullValue { get; set; }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Create a binding: {Binding [Key], Source=App.Localization, Mode=OneWay}
        // The Source is the LocalizationService directly — NOT a ViewModel path —
        // so PropertyChanged("Item[]") on the service reaches this binding.
        return new ReflectionBindingExtension($"[{Key}]")
        {
            Mode = BindingMode.OneWay,
            Source = App.Localization,
            FallbackValue = FallbackValue,
            TargetNullValue = TargetNullValue,
        }.ProvideValue(serviceProvider);
    }
}
