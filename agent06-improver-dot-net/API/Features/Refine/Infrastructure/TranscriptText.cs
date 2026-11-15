using System.Text;
using TranslationImprover.Features.Refine.Application;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// A transcript as it was given to the run — the lines before the marker, the lines to refine, the lines after —
/// and the form of its text: byte order mark, line ends, a line end after the last line. The refined file is
/// written in that same form (<see cref="Compose"/>). It used to be written with a byte order mark, the line
/// ends of the machine and one line more than the source, whatever the source was.
/// </summary>
public sealed class TranscriptText
{
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    /// <summary>Lines up to and with the opening marker; each line ends with "\n".</summary>
    public IReadOnlyList<string> Header { get; }

    /// <summary>The lines the run refines; each line ends with "\n".</summary>
    public IReadOnlyList<string> Content { get; }

    /// <summary>The closing marker and what follows it; each line ends with "\n".</summary>
    public IReadOnlyList<string> Footer { get; }

    public bool HasByteOrderMark { get; }

    /// <summary>"\r\n" when the source has such a line end anywhere, else "\n".</summary>
    public string NewLine { get; }

    public bool EndsWithNewLine { get; }

    private TranscriptText(IReadOnlyList<string> header, IReadOnlyList<string> content, IReadOnlyList<string> footer,
        bool hasByteOrderMark, string newLine, bool endsWithNewLine)
    {
        Header = header;
        Content = content;
        Footer = footer;
        HasByteOrderMark = hasByteOrderMark;
        NewLine = newLine;
        EndsWithNewLine = endsWithNewLine;
    }

    /// <summary>The transcript of a request: its inline content, else the file under <paramref name="artifactRoot"/>.</summary>
    public static async Task<TranscriptText> ReadAsync(RefineJobRequest request, string artifactRoot, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.InputContent))
            return FromText(request.InputContent!, hasByteOrderMark: false);

        var path = Path.Combine(artifactRoot, RefineJobRequestMapper.RelativePath(request.InputFilePath!));
        return FromBytes(await File.ReadAllBytesAsync(path, cancellationToken));
    }

    public static TranscriptText FromBytes(byte[] bytes)
    {
        var hasBom = bytes.Length >= 3 && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2];
        var text = hasBom ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3) : Encoding.UTF8.GetString(bytes);
        return FromText(text, hasBom);
    }

    public static TranscriptText FromText(string text, bool hasByteOrderMark)
    {
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
            hasByteOrderMark = true;
        }

        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var plain = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var endsWithNewLine = plain.EndsWith('\n');
        if (endsWithNewLine)
            plain = plain[..^1];

        var lines = plain.Length == 0 && !endsWithNewLine
            ? new List<string>()
            : plain.Split('\n').Select(l => l + "\n").ToList();
        TranscriptParser.ParseStructure(lines, out var header, out var content, out var footer);
        return new TranscriptText(header, content, footer, hasByteOrderMark, newLine, endsWithNewLine);
    }

    /// <summary>The whole text with <paramref name="content"/> in place of the refined lines, in the source's line ends.</summary>
    public string Compose(IReadOnlyList<string> content)
    {
        var lines = Header.Concat(content).Concat(Footer).Select(l => l.TrimEnd('\n', '\r'));
        var text = string.Join(NewLine, lines);
        return EndsWithNewLine ? text + NewLine : text;
    }

    /// <summary>Writes <see cref="Compose"/> as UTF-8, with a byte order mark only when the source has one.</summary>
    public async Task WriteAsync(string path, IReadOnlyList<string> content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, Compose(content), new UTF8Encoding(HasByteOrderMark), cancellationToken);
    }
}
