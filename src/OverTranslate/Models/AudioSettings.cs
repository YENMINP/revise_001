using System.Text.Json.Serialization;

namespace OverTranslate.Models;

/// <summary>Which local Whisper model 系統音訊翻譯 runs inference with.</summary>
/// <remarks>
/// A user-facing choice rather than a fixed constant, for the same reason 即時翻譯 lets the reader
/// pick how many blocks to frame: how much CPU/GPU headroom is available while a game or video is
/// also running is something only the person watching can judge. Tiny/Base favour reaction speed;
/// Small/Medium favour accuracy at the cost of a slower, heavier model resident in memory.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AudioAsrModelSize
{
    Tiny,
    Base,
    Small,
    Medium,
}

/// <summary>
/// Everything 系統音訊翻譯 keeps between sittings, under one key — see <see cref="RealtimeSettings"/>
/// for why grouped settings are the shape new features should copy.
/// </summary>
/// <remarks>
/// This feature runs entirely on-device (capture, VAD, ASR all local): unlike 即時翻譯 it has no
/// engine/API-key pair of its own to keep here, because only the translation step leaves the
/// machine, and that step reuses whichever engine 設定 already has selected rather than asking the
/// reader to pick twice for something they already answered once.
/// </remarks>
public class AudioSettings
{
    /// <summary>Whether 系統音訊翻譯 is available from the tray menu / global shortcut at all.</summary>
    public bool Enabled { get; set; } = false;

    /// <inheritdoc cref="AudioAsrModelSize"/>
    public AudioAsrModelSize ModelSize { get; set; } = AudioAsrModelSize.Small;

    /// <summary>
    /// The language spoken in the captured audio, or "auto" to let the ASR model detect it per
    /// segment. Unlike 即時翻譯's <see cref="RealtimeSettings.SourceLanguage"/>, "auto" is offered
    /// here: a wrong guess costs a rerun of one already-spoken sentence, not a silent frame that
    /// never gets a second look.
    /// </summary>
    public string SourceLanguage { get; set; } = "auto";

    /// <inheritdoc cref="RealtimeSettings.TargetLanguage"/>
    public string TargetLanguage { get; set; } = LanguageData.DefaultTargetLanguage;

    /// <summary>
    /// Milliseconds of continuous silence that ends a spoken segment and sends it to the ASR model.
    /// </summary>
    /// <remarks>
    /// 350–400ms is the sweet spot worked out during design: shorter and a speaker's natural
    /// mid-sentence pauses fragment into multiple segments with broken meaning; longer and the
    /// subtitle visibly lags behind speech. Exposed as a setting rather than a constant because a
    /// fast-talking narrator and a slow-paced dialogue scene do not share one right answer.
    /// </remarks>
    public int SilenceThresholdMs { get; set; } = 380;

    /// <summary>
    /// Longest a segment may run before it is force-flushed to the ASR model even without a silence
    /// gap, so a long uninterrupted line of dialogue is not held back indefinitely.
    /// </summary>
    public double MaxSegmentSeconds { get; set; } = 9.0;

    /// <inheritdoc cref="RealtimeSettings.TextColor"/>
    public string TextColor { get; set; } = Services.Realtime.RealtimeSubtitleColors.DefaultText;

    /// <inheritdoc cref="RealtimeSettings.ScrimColor"/>
    public string ScrimColor { get; set; } = Services.Realtime.RealtimeSubtitleColors.DefaultScrim;

    /// <inheritdoc cref="RealtimeSettings.ScrimOpacity"/>
    public int ScrimOpacity { get; set; } = Services.Realtime.RealtimeSubtitleColors.DefaultScrimOpacity;
}
