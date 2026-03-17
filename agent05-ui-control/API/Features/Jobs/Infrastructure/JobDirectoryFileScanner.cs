using System.Text;
using TagLib;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>Scans a job directory and returns display-friendly file info for UI (text: line count; audio: duration, size; all: name, size).</summary>
public static class JobDirectoryFileScanner
{
    private static readonly string[] TextExtensions = { ".md", ".txt", ".json", ".srt", ".vtt", ".csv", ".xml", ".log" };
    private static readonly string[] AudioExtensions = { ".m4a", ".mp3", ".wav", ".ogg", ".flac", ".bin" };

    public static IReadOnlyList<JobFileInfo> Scan(string dirPath)
    {
        var list = new List<JobFileInfo>();
        try
        {
            var dir = new DirectoryInfo(dirPath);
            foreach (var fi in dir.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                var ext = fi.Extension;
                var sizeBytes = fi.Length;
                if (TextExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    var lineCount = CountLines(fi.FullName);
                    list.Add(new JobFileInfo
                    {
                        Name = fi.Name,
                        Kind = "text",
                        SizeBytes = sizeBytes,
                        LineCount = lineCount
                    });
                }
                else if (AudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    var durationSeconds = GetAudioDurationSeconds(fi.FullName);
                    list.Add(new JobFileInfo
                    {
                        Name = fi.Name,
                        Kind = "audio",
                        SizeBytes = sizeBytes,
                        DurationSeconds = durationSeconds
                    });
                }
                else
                {
                    list.Add(new JobFileInfo
                    {
                        Name = fi.Name,
                        Kind = "other",
                        SizeBytes = sizeBytes
                    });
                }
            }
        }
        catch
        {
            // return whatever we collected
        }
        return list;
    }

    private static int? CountLines(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            var count = 0;
            while (reader.ReadLine() != null) count++;
            return count;
        }
        catch
        {
            return null;
        }
    }

    private static double? GetAudioDurationSeconds(string filePath)
    {
        try
        {
            using var tfile = TagLib.File.Create(filePath);
            var duration = tfile.Properties.Duration;
            return duration.TotalSeconds;
        }
        catch
        {
            return null;
        }
    }
}
