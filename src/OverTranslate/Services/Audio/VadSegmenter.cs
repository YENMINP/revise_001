namespace OverTranslate.Services.Audio;

/// <summary>
/// Accumulates a continuous PCM stream into spoken segments, each handed off whole once the
/// speaker pauses (or has spoken too long without pausing) — see <see cref="Models.AudioSettings"/>
/// for the two thresholds this is built from.
/// </summary>
/// <remarks>
/// Deliberately not a rolling/overlapping-window design. Whisper-family models are not built for
/// truly continuous streaming, and an overlap-plus-dedupe approach that would give lower perceived
/// latency also means merging the same words recognised twice — real complexity for a first cut.
/// The trade this design makes instead: subtitles appear only once a sentence has fully finished,
/// with no partial/live-updating text. That is a deliberate, cheaper corner — see
/// <see cref="Views.Audio.AudioSubtitleWindow"/> for how the fade-in on arrival is meant to soften
/// the "appears all at once" feel this implies.
///
/// The current voice/silence test (<see cref="ComputeRms"/>) is a plain energy threshold — it will
/// misfire against loud background music or game sound effects sitting under dialogue. Swapping it
/// for a proper model (Silero VAD, run locally via ONNX Runtime — already a project dependency
/// through OcrService) is the natural next step; the frame-in/segment-out shape here does not need
/// to change to do that, only <see cref="ComputeRms"/>'s replacement.
/// </remarks>
internal sealed class VadSegmenter
{
    private readonly double _silenceThresholdRms;
    private readonly TimeSpan _silenceDuration;
    private readonly TimeSpan _maxSegmentLength;

    private readonly List<float> _buffer = [];
    private TimeSpan _silenceAccumulated = TimeSpan.Zero;
    private TimeSpan _speechAccumulated = TimeSpan.Zero;
    private bool _hasSpeech;

    /// <summary>Fired once per finished segment, on whatever thread pushed the frame that closed it.</summary>
    public event Action<float[]>? OnSegmentReady;

    public VadSegmenter(Models.AudioSettings settings, double silenceThresholdRms = 0.01)
    {
        _silenceThresholdRms = silenceThresholdRms;
        _silenceDuration = TimeSpan.FromMilliseconds(settings.SilenceThresholdMs);
        _maxSegmentLength = TimeSpan.FromSeconds(settings.MaxSegmentSeconds);
    }

    /// <param name="frame">Mono float32 samples, one short frame (20-30ms is the usual size).</param>
    /// <param name="frameDuration">How much audio <paramref name="frame"/> represents.</param>
    public void PushFrame(float[] frame, TimeSpan frameDuration)
    {
        bool isSpeech = ComputeRms(frame) > _silenceThresholdRms;

        if (isSpeech)
        {
            _buffer.AddRange(frame);
            _speechAccumulated += frameDuration;
            _silenceAccumulated = TimeSpan.Zero;
            _hasSpeech = true;
        }
        else if (_hasSpeech)
        {
            // Still inside an in-progress segment — a pause might be mid-sentence breath, not the end.
            _buffer.AddRange(frame);
            _silenceAccumulated += frameDuration;
            _speechAccumulated += frameDuration;

            if (_silenceAccumulated >= _silenceDuration)
            {
                Flush();
                return;
            }
        }

        // Force-flush protection: don't let a long uninterrupted line withhold a result indefinitely.
        if (_hasSpeech && _speechAccumulated >= _maxSegmentLength)
            Flush();
    }

    private void Flush()
    {
        if (_buffer.Count > 0)
            OnSegmentReady?.Invoke([.. _buffer]);
        Reset();
    }

    private void Reset()
    {
        _buffer.Clear();
        _silenceAccumulated = TimeSpan.Zero;
        _speechAccumulated = TimeSpan.Zero;
        _hasSpeech = false;
    }

    private static double ComputeRms(float[] frame)
    {
        double sumSquares = 0;
        foreach (var sample in frame) sumSquares += sample * sample;
        return Math.Sqrt(sumSquares / frame.Length);
    }
}
