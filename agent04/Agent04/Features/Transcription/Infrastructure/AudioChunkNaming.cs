namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Chunk split uses ffmpeg <c>-c copy</c>; the output muxer must match the source streams (e.g. PCM in WAV, not in M4A).
/// </summary>
public static class AudioChunkNaming
{
    /// <summary>
    /// Replaces the extension in <paramref name="namingPatternWithBaseReplaced"/> with that of <paramref name="sourcePath"/>
    /// when they differ, so stream-copy chunk outputs use a compatible container.
    /// </summary>
    public static string AlignOutputExtensionWithSource(string namingPatternWithBaseReplaced, string sourcePath)
    {
        var sourceExt = Path.GetExtension(sourcePath);
        if (string.IsNullOrEmpty(sourceExt))
            return namingPatternWithBaseReplaced;

        var fileName = Path.GetFileName(namingPatternWithBaseReplaced);
        var currentExt = Path.GetExtension(fileName);
        if (string.Equals(currentExt, sourceExt, StringComparison.OrdinalIgnoreCase))
            return namingPatternWithBaseReplaced;

        var newFileName = string.IsNullOrEmpty(currentExt)
            ? fileName + sourceExt
            : Path.ChangeExtension(fileName, sourceExt);

        var dir = Path.GetDirectoryName(namingPatternWithBaseReplaced);
        return string.IsNullOrEmpty(dir) ? newFileName : Path.Combine(dir, newFileName);
    }
}
