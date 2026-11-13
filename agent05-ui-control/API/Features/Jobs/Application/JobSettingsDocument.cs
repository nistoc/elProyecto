using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace XtractManager.Features.Jobs.Application;

/// <summary>
/// Settings the operator chose for one job at upload: silence compression, the pause that is kept, the model.
/// They are written to <c>job_settings.json</c> in the job folder; the transcriber (agent04) puts them on top
/// of its shared config, so the shared <c>config/default.json</c> stays as it is for every other job.
/// The keys and their limits are the ones agent04 accepts (<c>JobSettingsFile</c> there).
/// </summary>
public static class JobSettingsDocument
{
    public const string FileName = "job_settings.json";
    public const int SchemaVersion = 1;

    public const double MinSilenceKeepSec = 0.05;
    public const double MaxSilenceKeepSec = 5.0;

    private static readonly Regex ModelId = new(@"^[A-Za-z0-9][A-Za-z0-9._:\-]{0,79}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Turns the <c>settings</c> form field (a JSON object of transcription keys) into the text of the file.
    /// </summary>
    /// <param name="formValue">For example <c>{"silence_keep_sec":1.0,"model":"whisper-1"}</c>; empty means no settings.</param>
    /// <param name="fileJson">The text of <c>job_settings.json</c>, or null when there is nothing to write.</param>
    /// <param name="error">Why the value was refused.</param>
    public static bool TryBuild(string? formValue, out string? fileJson, out string? error)
    {
        fileJson = null;
        error = null;
        if (string.IsNullOrWhiteSpace(formValue))
            return true;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(formValue);
        }
        catch (JsonException)
        {
            error = "settings must be a JSON object.";
            return false;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "settings must be a JSON object.";
                return false;
            }

            var transcription = new JsonObject();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "silence_compress_enabled":
                        if (p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        {
                            error = "settings.silence_compress_enabled must be true or false.";
                            return false;
                        }

                        transcription[p.Name] = p.Value.GetBoolean();
                        break;

                    case "silence_keep_sec":
                        if (p.Value.ValueKind != JsonValueKind.Number
                            || !p.Value.TryGetDouble(out var keep)
                            || double.IsNaN(keep)
                            || keep < MinSilenceKeepSec
                            || keep > MaxSilenceKeepSec)
                        {
                            error = string.Create(
                                CultureInfo.InvariantCulture,
                                $"settings.silence_keep_sec must be a number from {MinSilenceKeepSec} to {MaxSilenceKeepSec}.");
                            return false;
                        }

                        transcription[p.Name] = keep;
                        break;

                    case "model":
                        var model = p.Value.ValueKind == JsonValueKind.String ? (p.Value.GetString() ?? "").Trim() : "";
                        if (!ModelId.IsMatch(model))
                        {
                            error = "settings.model must be a model id (letters, digits, '.', '_', ':', '-').";
                            return false;
                        }

                        transcription[p.Name] = model;
                        break;

                    default:
                        error = $"settings.{p.Name} is not a setting of a job (allowed: silence_compress_enabled, silence_keep_sec, model).";
                        return false;
                }
            }

            if (transcription.Count == 0)
                return true;

            var file = new JsonObject
            {
                ["schema_version"] = SchemaVersion,
                ["transcription"] = transcription,
            };
            fileJson = file.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            return true;
        }
    }
}
