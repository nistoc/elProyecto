using System.Globalization;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Sessions.Application;

/// <summary>
/// What a run of a track of the recordings folder goes with when it names nothing itself: the model by
/// the kind of the track, and the price used to say what a run may cost before it is started.
/// </summary>
public static class RecordingRunDefaults
{
    /// <summary>Upper bound of the price of the diarize model per minute of audio, measured by tokens on 2026-11-13.</summary>
    public const double EstimateUsdPerMinute = 0.017;

    public const string EstimateKey = "Recordings:EstimateUsdPerMinute";

    /// <summary>
    /// The model a participant track goes to when the run names none. A participant track is one voice, so
    /// it needs no speakers; this model writes each language in its own script (the transcriber asks it for
    /// a chunk whole and in pieces). The mixed track and a plain upload keep the transcriber's shared model.
    /// </summary>
    public const string ParticipantTrackModel = "gpt-transcribe";

    /// <summary>Config key of that model; an empty value leaves participant tracks on the shared model.</summary>
    public const string ParticipantTrackModelKey = "Recordings:ParticipantTrackModel";

    /// <summary>
    /// Upper bound of the price of that model per minute of a track: the audio is asked twice at $0.0045 a
    /// minute, and a few pieces once more. The runs of 2026-11-14 cost about $0.006 a minute.
    /// </summary>
    public const double ParticipantTrackEstimateUsdPerMinute = 0.01;

    public const string ParticipantTrackEstimateKey = "Recordings:ParticipantTrackEstimateUsdPerMinute";

    /// <summary>
    /// The model of a recording that has only the mixed file, when a batch of days starts it and names no
    /// model: Spanish is written in Latin letters, there are no speakers. A run started alone from the
    /// lessons list keeps the shared model as before.
    /// </summary>
    public const string MixedOnlyTrackModel = "gpt-transcribe";

    /// <summary>Config key of that model; an empty value leaves such recordings on the shared model.</summary>
    public const string MixedOnlyTrackModelKey = "Recordings:MixedOnlyTrackModel";

    /// <summary>
    /// Upper bound of the price of that model per minute of a mixed recording: the 13 recordings of
    /// 2026-11-19 cost $0.0089 a minute (silence cut out, the chunk asked whole and in pieces); a quarter on top.
    /// </summary>
    public const double PlainMixedEstimateUsdPerMinute = 0.0112;

    public const string PlainMixedEstimateKey = "Recordings:PlainMixedEstimateUsdPerMinute";

    /// <summary>
    /// The model named under <paramref name="key"/>: <paramref name="fallback"/> when the key is absent, null
    /// when it is empty or is not a model id (<paramref name="notAModel"/> tells the two apart).
    /// </summary>
    public static string? Model(IConfiguration configuration, string key, string fallback, out bool notAModel)
    {
        notAModel = false;
        var raw = configuration[key];
        if (raw == null)
            return fallback;
        var model = raw.Trim();
        if (model.Length == 0)
            return null;
        if (JobSettingsDocument.IsModelId(model))
            return model;
        notAModel = true;
        return null;
    }

    public static double Price(IConfiguration configuration, string key, double fallback)
    {
        var raw = configuration[key];
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;
    }

    /// <summary>Upper bound of the price of a minute of a track of this kind sent to this model (null = the shared one).</summary>
    public static double UsdPerMinute(IConfiguration configuration, string trackKind, string? model)
    {
        if (model == null || !model.StartsWith("gpt-transcribe", StringComparison.OrdinalIgnoreCase))
            return Price(configuration, EstimateKey, EstimateUsdPerMinute);
        return trackKind == LessonBatchPlanner.KindParticipant
            ? Price(configuration, ParticipantTrackEstimateKey, ParticipantTrackEstimateUsdPerMinute)
            : Price(configuration, PlainMixedEstimateKey, PlainMixedEstimateUsdPerMinute);
    }
}
