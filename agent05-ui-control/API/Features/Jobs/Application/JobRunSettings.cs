namespace XtractManager.Features.Jobs.Application;

/// <summary>
/// The settings a job was transcribed with, as far as its folder tells. A value that the folder does not
/// tell is null: nothing is filled in from the shared config of today, a job of last month ran with another one.
/// </summary>
/// <param name="Source">
/// Where the values come from: <c>run</c> — the transcriber's own record (<c>run_settings.json</c>), exact;
/// <c>artifacts</c> — what the run left in the folder (a job from before the record existed);
/// <c>job_settings</c> — only the operator's settings, the job has not run yet.
/// </param>
/// <param name="Model">The model; null when it was not recorded.</param>
/// <param name="SilenceCompress">Whether pauses were cut out; null when the folder does not tell.</param>
/// <param name="SilenceKeepSec">How much of a pause was kept; set only when pauses were cut out.</param>
/// <param name="FromJobSettings">Keys the operator set for this job (<c>job_settings.json</c>).</param>
public sealed record JobRunSettings(
    string Source,
    string? Model,
    bool? SilenceCompress,
    double? SilenceKeepSec,
    IReadOnlyList<string> FromJobSettings)
{
    public const string SourceRun = "run";
    public const string SourceArtifacts = "artifacts";
    public const string SourceJobSettings = "job_settings";
}
