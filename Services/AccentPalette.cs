using System.Collections.Generic;

namespace FH6Mod.Services;

public sealed record Accent(string Name, string Base, string Hover, string Pressed, string Muted);

public static class AccentPalette
{
    public const string DefaultName = "Forza Orange";

    public static readonly IReadOnlyList<Accent> All =
    [
        new("Forza Orange",  "#FF6A1F", "#FF8845", "#E55400", "#5C2810"),
        new("Crimson",       "#E63946", "#F25B66", "#C42A38", "#4D1216"),
        new("Magenta",       "#C026D3", "#D54AE6", "#9A1AAA", "#3F0E45"),
        new("Violet",        "#8B5CF6", "#A47DFB", "#6E3FD9", "#2D1A50"),
        new("Royal Blue",    "#3B82F6", "#5A99FA", "#266BD8", "#142A52"),
        new("Cyan",          "#06B6D4", "#22CCEE", "#0492AD", "#063843"),
        new("Emerald",       "#10B981", "#34D399", "#0E9468", "#073E2A"),
        new("Lime",          "#84CC16", "#A5E031", "#6BA80E", "#2D4408"),
        new("Amber",         "#F59E0B", "#FBB72E", "#D17F03", "#4F3304"),
        new("Rose",          "#F43F5E", "#FB5F7A", "#D8224A", "#4E0F1C"),
        new("Neon",          "#D946EF", "#E96BF5", "#B825D3", "#460D51"),
        new("Pastel",        "#F472B6", "#F89BC8", "#E0509E", "#561D38"),
        new("Forest",        "#059669", "#34D399", "#047857", "#023D1A"),
        new("Ocean",         "#0EA5E9", "#38BDF8", "#0284C7", "#0C3348"),
        new("Dark",          "#6B7280", "#9CA3AF", "#4B5563", "#1F2937"),
        new("Classic",       "#F97316", "#FB923C", "#EA580C", "#5C2806"),
    ];

    public static Accent ByName(string? name)
    {
        if (!string.IsNullOrEmpty(name))
            foreach (var a in All) if (a.Name == name) return a;
        return All[0];
    }
}
