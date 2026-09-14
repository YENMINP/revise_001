using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OverTranslate.Models;

namespace OverTranslate.Views.Audio;

/// <summary>
/// A single fixed subtitle bar, bottom-center of the screen. Deliberately not click-through today
/// (WS_EX_TRANSPARENT) — unlike 即時翻譯's overlays, which sit over the exact text they replace and
/// must never intercept a click meant for the game underneath, this bar occupies empty space at the
/// bottom of the screen. Revisit if that turns out to be wrong for fullscreen-exclusive games; see
/// <c>AlwaysOnTop</c>/<c>WindowStyles</c> for how the project already solves click-through elsewhere
/// if so.
/// </summary>
internal sealed partial class AudioSubtitleWindow : Window
{
    private readonly AudioSettings _settings;

    public AudioSubtitleWindow(AudioSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        ApplyColors();
        Loaded += (_, _) => PositionAtBottomOfScreen();
    }

    private void ApplyColors()
    {
        // TODO: confirm against RealtimeSubtitleColors whether it already exposes a
        // "#RRGGBB" (+opacity) -> Brush helper — reuse it instead of this inline parse if so, for
        // one shared definition of how these strings are interpreted.
        Scrim.Background = new SolidColorBrush(WithOpacity(_settings.ScrimColor, _settings.ScrimOpacity));
        var textBrush = new SolidColorBrush(ParseColor(_settings.TextColor));
        TranslatedText.Foreground = textBrush;
        OriginalText.Foreground = textBrush;
    }

    private static System.Windows.Media.Color ParseColor(string hex) =>
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);

    private static System.Windows.Media.Color WithOpacity(string hex, int opacityPercent)
    {
        var c = ParseColor(hex);
        c.A = (byte)Math.Clamp(opacityPercent * 255 / 100, 0, 255);
        return c;
    }

    private void PositionAtBottomOfScreen()
    {
        // Physical-pixel/DPI handling deliberately left to whatever the project's existing
        // ScreenGeometry helper does elsewhere (OverlayWindow, RealtimeBlockWindow) — duplicating a
        // second DPI-aware placement routine here would be a second place for that math to drift
        // out of sync with the first.
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + workArea.Height * 0.85 - ActualHeight;
    }

    /// <summary>Displays one finished line and re-triggers the fade-in.</summary>
    public void ShowLine(string original, string translated)
    {
        TranslatedText.Text = translated;
        OriginalText.Text = original;
        PositionAtBottomOfScreen(); // height changed with the new text

        var fadeIn = new Storyboard();
        fadeIn.Children.Add(new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            { });
        Storyboard.SetTarget(fadeIn.Children[0], Scrim);
        Storyboard.SetTargetProperty(fadeIn.Children[0], new PropertyPath(OpacityProperty));

        var rise = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(180));
        Storyboard.SetTarget(rise, FadeInOffset);
        Storyboard.SetTargetProperty(rise, new PropertyPath(TranslateTransform.YProperty));
        fadeIn.Children.Add(rise);

        fadeIn.Begin();
    }

    /// <summary>
    /// A segment failed to transcribe/translate. Deliberately silent rather than showing an error
    /// bubble mid-video — see RealtimeTranslationSession's own "retry quietly" handling for failed
    /// passes, which this mirrors: one dropped line is not worth interrupting what's playing for.
    /// </summary>
    public void ShowTransientError() { /* intentionally no-op for now — see remarks */ }
}