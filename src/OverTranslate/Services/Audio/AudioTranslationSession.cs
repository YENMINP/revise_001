using NAudio.MediaFoundation;
using NLog;
using OverTranslate.Models;

namespace OverTranslate.Services.Audio;

public sealed record AudioSubtitleLine(string Original, string Translated);

/// <summary>
/// Owns one sitting of 系統音訊翻譯 end to end: loopback capture, VAD segmentation, local ASR, and
/// handing the recognised text to the shared <see cref="AppServices.Translation"/> engine — mirrors
/// <c>RealtimeTranslationSession</c>'s role for 即時翻譯, but audio has no per-frame OCR pass to
/// throttle against, so this is considerably smaller.
/// </summary>
/// <remarks>
/// Runs on the shared engines in <see cref="AppServices"/>, same reasoning as 即時翻譯: translation
/// is one HTTP-backed service either way, and a second copy would only split rate limits for no
/// benefit. The ASR engine is <em>not</em> shared with anything today (nothing else does speech
/// recognition), but is still homed on <see cref="AppServices"/> so a later feature that also wants
/// local ASR does not load a second copy of the model.
/// </remarks>
internal sealed class AudioTranslationSession : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    static AudioTranslationSession()
    {
        // Required once per process before MediaFoundationResampler will work — see
        // SystemAudioCapture. Cheap and idempotent; safe to call even if never used.
        MediaFoundationApi.Startup();
    }

    private readonly AudioSettings _settings;
    private readonly SystemAudioCapture _capture;
    private readonly VadSegmenter _segmenter;
    // Serializes ASR + translation so a burst of short utterances queues instead of the CPU/GPU
    // being asked to run two Whisper passes at once — see design discussion: audio and OCR
    // inference should not compete unthrottled for the same resources.
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    public event Action<AudioSubtitleLine>? OnSubtitleReady;
    public event Action<Exception>? OnFailure;

    public AudioTranslationSession(AudioSettings settings)
    {
        _settings = settings;
        _segmenter = new VadSegmenter(settings);
        _segmenter.OnSegmentReady += segment => _ = HandleSegmentAsync(segment);
        _capture = new SystemAudioCapture((frame, duration) => _segmenter.PushFrame(frame, duration));
    }

    public void Start() => _capture.Start();

    public void Stop()
    {
        _cts.Cancel();
        _capture.Stop();
    }

    private async Task HandleSegmentAsync(float[] segment)
    {
        if (_cts.IsCancellationRequested) return;

        await _inferenceGate.WaitAsync(_cts.Token);
        try
        {
            var original = await AppServices.AudioAsr.TranscribeAsync(
                segment, _settings.SourceLanguage, _cts.Token);
            if (string.IsNullOrWhiteSpace(original)) return;

            var apiKey = SettingsService.Instance.Current.ApiKey;
            var provider = SettingsService.Instance.Current.Provider;
            // Bounds is meaningless for a line with no on-screen position — the audio subtitle
            // window ignores it and reads OriginalText/TranslatedText only. See OcrTextBlock.
            var block = new OcrTextBlock(original, default);

            // TranslateAsync's sourceLang here is the language Whisper recognised the segment as
            // (or, if it ran with "auto", whatever the ASR settled on for this segment) — not
            // necessarily _settings.SourceLanguage verbatim. TODO: confirm against
            // ITranslationProvider whether the resilient providers can take "auto" here the same
            // way OCR's 自動 flows through, or whether this needs the ASR's detected-language code.
            var (results, _) = await AppServices.Translation.TranslateAsync(
                [block], _settings.SourceLanguage, _settings.TargetLanguage, apiKey,
                cancellationToken: _cts.Token, engine: provider);

            if (_cts.IsCancellationRequested || results.Count == 0) return;

            OnSubtitleReady?.Invoke(new AudioSubtitleLine(original, results[0].TranslatedText));
        }
        catch (OperationCanceledException)
        {
            // Session ended mid-flight — expected, not a failure.
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Audio segment failed to transcribe/translate");
            OnFailure?.Invoke(ex);
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    public void Dispose()
    {
        Stop();
        _capture.Dispose();
        _cts.Dispose();
    }
}
