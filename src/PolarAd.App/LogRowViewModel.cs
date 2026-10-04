using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using PolarAd.Core.Models;

namespace PolarAd.App;

public sealed class LogRowViewModel
{
    private static readonly Brush BlockedBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x8A, 0x2E));
    private static readonly Brush AllowedBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x5B));

    public string TimestampDisplay { get; }
    public string AppName { get; }
    public string Domain { get; }
    public string DecisionDisplay { get; }
    public string MatchedRule { get; }

    public string Glyph { get; }
    public Brush GlyphBrush { get; }
    public string Summary { get; }

    public LogRowViewModel(BlockLogEntry entry)
    {
        TimestampDisplay = entry.Timestamp.LocalDateTime.ToString("HH:mm:ss");
        AppName = entry.AppName;
        Domain = entry.Domain;
        MatchedRule = entry.MatchedRule;

        bool blocked = entry.Decision is BlockDecision.BlockedByList or BlockDecision.BlockedByUserRule or BlockDecision.BlockedByBuiltIn;
        DecisionDisplay = entry.Decision switch
        {
            BlockDecision.BlockedByList => "차단 (공개 목록)",
            BlockDecision.BlockedByUserRule => "차단 (내 규칙)",
            BlockDecision.BlockedByBuiltIn => "차단 (기본 규칙)",
            BlockDecision.AllowedByUserAllowlist => "허용 (허용 목록)",
            _ => "허용",
        };

        Glyph = blocked ? "" : "";
        GlyphBrush = blocked ? BlockedBrush : AllowedBrush;
        Summary = $"{(blocked ? "차단" : "허용")}  {entry.Domain}";
    }
}
