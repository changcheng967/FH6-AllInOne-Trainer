using System.Collections.Generic;
using Material.Icons;

namespace FH6Mod.ViewModels.Pages;

public abstract class PageViewModelBase : ViewModelBase
{
    public abstract string PageTitle { get; }
    public abstract string PageSubtitle { get; }
    public abstract MaterialIconKind PageIcon { get; }
    public virtual IReadOnlyList<FeatureRow> Features { get; } = [];

    protected override void OnLanguageChanged(string code)
    {
        base.OnLanguageChanged(code);
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
    }
}

public enum FeatureStatus { Working, Untested, NotWorking, Locked }

public sealed record FeatureRow(string Name, string? Hint, FeatureStatus Status);
