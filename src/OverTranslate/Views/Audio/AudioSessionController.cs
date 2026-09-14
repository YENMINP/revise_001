using OverTranslate.Models;
using OverTranslate.Services;
using OverTranslate.Services.Audio;

namespace OverTranslate.Views.Audio;

/// <summary>
/// Owns one 系統音訊翻譯 sitting end to end: the session and the subtitle window — mirrors
/// <see cref="Realtime.RealtimeSessionController"/>'s shape at a fraction of the size, since this
/// feature has no edit layer, no per-block windows and no capture-backend to hand off between them.
/// </summary>
internal sealed class AudioSessionController
{
    public static AudioSessionController Instance { get; } = new();

    private AudioTranslationSession? _session;
    private AudioSubtitleWindow? _subtitleWindow;

    public bool IsRunning => _session is not null;

    public void Toggle()
    {
        if (IsRunning) Stop();
        else Start();
    }

    public void Start()
    {
        if (IsRunning) return;

        var settings = SettingsService.Instance.Current.Audio;
        _subtitleWindow = new AudioSubtitleWindow(settings);
        _subtitleWindow.Show();

        _session = new AudioTranslationSession(settings);
        _session.OnSubtitleReady += line =>
            _subtitleWindow.Dispatcher.Invoke(() => _subtitleWindow.ShowLine(line.Original, line.Translated));
        _session.OnFailure += _ =>
            _subtitleWindow.Dispatcher.Invoke(() => _subtitleWindow.ShowTransientError());

        _session.Start();
    }

    public void Stop()
    {
        _session?.Dispose();
        _session = null;

        _subtitleWindow?.Close();
        _subtitleWindow = null;
    }
}
