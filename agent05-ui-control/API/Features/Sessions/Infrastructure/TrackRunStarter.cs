using System.Globalization;
using System.Text;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Sessions.Application;

namespace XtractManager.Features.Sessions.Infrastructure;

/// <summary>The job of a track could not be prepared; the job that was begun is removed again.</summary>
public sealed class TrackRunStartException : Exception
{
    public TrackRunStartException(string jobId, Exception inner)
        : base("Failed to copy the track into a job folder.", inner) => JobId = jobId;

    public string JobId { get; }
}

/// <summary>
/// Starts a job for one track of the recordings folder: the file is copied into a new job folder, the
/// folder is told which track it transcribes, and the job goes into the usual (paid) pipeline. One way in
/// for a run started from the lessons list and for a batch of days.
/// </summary>
public static class TrackRunStarter
{
    /// <param name="settingsFileJson">Content of the job's <c>job_settings.json</c>; null = no settings of its own.</param>
    /// <returns>Id of the queued job.</returns>
    public static async Task<string> StartAsync(
        IJobStore store,
        IJobWorkspace workspace,
        IPipelineQueue queue,
        RecordingTrackFile found,
        string? settingsFileJson,
        CancellationToken ct = default)
    {
        var tags = new List<string> { "lesson:" + found.Session.Date, "track:" + found.Track.Kind };
        var jobId = await store.CreateAsync(new JobCreateInput(found.Track.FileName, tags), ct);
        try
        {
            await using (var source = new FileStream(
                             found.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                await workspace.SaveUploadedFileAsync(jobId, source, found.Track.FileName, ct);
            }

            var jobDir = workspace.GetJobDirectoryPath(jobId);
            await TrackSourceFile.SaveAsync(
                jobDir,
                new TrackSource
                {
                    Track = found.Track.Id,
                    FileName = found.Track.FileName,
                    SizeBytes = found.Track.SizeBytes,
                    Session = found.Session.Id,
                    Period = found.Period.Id,
                    Kind = found.Track.Kind,
                    Participant = found.Track.Participant,
                    CreatedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                },
                ct);

            if (settingsFileJson != null)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(jobDir, JobSettingsDocument.FileName),
                    settingsFileJson,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    ct);
            }
        }
        catch (Exception ex)
        {
            await store.DeleteAsync(jobId, CancellationToken.None);
            throw new TrackRunStartException(jobId, ex);
        }

        queue.Enqueue(jobId);
        return jobId;
    }
}
