using System.Collections.Concurrent;
using XtractManager.Features.Sessions.Application;

namespace XtractManager.Features.Sessions.Infrastructure;

/// <summary>
/// The recordings folder from <c>Recordings:RootPath</c>. Empty or missing folder switches the lessons list off.
/// Audio lengths are read from the file headers once and remembered until the file changes.
/// </summary>
public sealed class RecordingsCatalog : IRecordingsCatalog
{
    public const string RootPathKey = "Recordings:RootPath";

    private readonly string? _rootPath;
    private readonly ConcurrentDictionary<string, (long Size, long ModifiedTicks, double? Seconds)> _durations =
        new(StringComparer.OrdinalIgnoreCase);

    public RecordingsCatalog(IConfiguration configuration, IHostEnvironment environment, ILogger<RecordingsCatalog> logger)
    {
        var raw = configuration[RootPathKey]?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            logger.LogInformation("Recordings folder is not configured ({Key}); the lessons list is off", RootPathKey);
            return;
        }

        var full = Path.IsPathRooted(raw)
            ? Path.GetFullPath(raw)
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath ?? Directory.GetCurrentDirectory(), raw));
        if (!Directory.Exists(full))
        {
            logger.LogWarning("Recordings folder {Path} ({Key}) does not exist; the lessons list is off", full, RootPathKey);
            return;
        }

        _rootPath = full;
        logger.LogInformation("Recordings folder ({Key}): {Path} (read only)", RootPathKey, full);
    }

    public bool Enabled => _rootPath != null;

    public string? RootPath => _rootPath;

    public IReadOnlyList<RecordingSession> Scan() =>
        _rootPath == null ? Array.Empty<RecordingSession>() : RecordingsScanner.Scan(_rootPath, Duration);

    public RecordingTrackFile? FindTrack(string? trackId)
    {
        var wanted = (trackId ?? "").Trim().Replace('\\', '/');
        if (_rootPath == null || wanted.Length == 0)
            return null;

        // The id is looked up among the files the scan lists, so a path that leads elsewhere is never opened.
        foreach (var session in Scan())
        foreach (var period in session.Periods)
        foreach (var track in period.Tracks)
        {
            if (!string.Equals(track.Id, wanted, StringComparison.Ordinal))
                continue;
            var full = Path.GetFullPath(Path.Combine(_rootPath, track.Id.Replace('/', Path.DirectorySeparatorChar)));
            return File.Exists(full) ? new RecordingTrackFile(session, period, track, full) : null;
        }

        return null;
    }

    private double? Duration(FileInfo file)
    {
        var size = file.Length;
        var modified = file.LastWriteTimeUtc.Ticks;
        if (_durations.TryGetValue(file.FullName, out var known) && known.Size == size && known.ModifiedTicks == modified)
            return known.Seconds;

        double? seconds;
        try
        {
            using var tag = TagLib.File.Create(file.FullName);
            var total = tag.Properties.Duration.TotalSeconds;
            seconds = total > 0 ? Math.Round(total, 1) : null;
        }
        catch
        {
            seconds = null;
        }

        _durations[file.FullName] = (size, modified, seconds);
        return seconds;
    }
}
