using System.Text;
using TranslationImprover.Features.Refine.Infrastructure;
using Xunit;

namespace API.Tests;

/// <summary>
/// The refined file has the form of its source: byte order mark, line ends and the line end after the last
/// line. A run that changes nothing gives the source back byte for byte.
/// </summary>
public class TranscriptTextTests
{
    private const string Body =
        "# Lesson\n>>>>>>>\n- 1.00 Olga: \"Vale, tengo una casa.\"\n- 2.00 Maria: \"Да, это мой дом.\"\n<<<<<\nfooter\n";

    private static byte[] Bytes(string text, bool bom) =>
        (bom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>()).Concat(new UTF8Encoding(false).GetBytes(text)).ToArray();

    private static async Task<byte[]> RoundTripAsync(byte[] source)
    {
        var path = Path.Combine(Path.GetTempPath(), "transcript-text-" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            var text = TranscriptText.FromBytes(source);
            await text.WriteAsync(path, text.Content, CancellationToken.None);
            return await File.ReadAllBytesAsync(path);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(false, false, true)]   // what the transcriber writes: no mark, "\n", a line end at the end
    [InlineData(true, true, true)]     // what the refiner itself used to write
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public async Task An_unchanged_transcript_is_written_back_byte_for_byte(bool bom, bool crlf, bool endsWithNewLine)
    {
        var text = endsWithNewLine ? Body : Body.TrimEnd('\n');
        if (crlf)
            text = text.Replace("\n", "\r\n");
        var source = Bytes(text, bom);

        Assert.Equal(source, await RoundTripAsync(source));
    }

    [Fact]
    public void The_lines_between_the_markers_are_the_content()
    {
        var text = TranscriptText.FromText(Body, hasByteOrderMark: false);

        Assert.Equal(new[] { "# Lesson\n", ">>>>>>>\n" }, text.Header);
        Assert.Equal(new[] { "- 1.00 Olga: \"Vale, tengo una casa.\"\n", "- 2.00 Maria: \"Да, это мой дом.\"\n" }, text.Content);
        Assert.Equal(new[] { "<<<<<\n", "footer\n" }, text.Footer);
        Assert.Equal("\n", text.NewLine);
        Assert.True(text.EndsWithNewLine);
        Assert.False(text.HasByteOrderMark);
    }

    [Fact]
    public void A_text_without_markers_is_content_only_and_gets_no_line_it_did_not_have()
    {
        var text = TranscriptText.FromText("a1\r\na2\r\n", hasByteOrderMark: false);

        Assert.Empty(text.Header);
        Assert.Equal(new[] { "a1\n", "a2\n" }, text.Content);
        Assert.Equal("A1\r\nA2\r\n", text.Compose(new[] { "A1\n", "A2\n" }));
        Assert.Empty(TranscriptText.FromText("", hasByteOrderMark: false).Content);
    }

    [Fact]
    public void A_mark_at_the_start_of_inline_text_is_not_a_part_of_the_first_line()
    {
        var text = TranscriptText.FromText("﻿a1\na2", hasByteOrderMark: false);

        Assert.True(text.HasByteOrderMark);
        Assert.Equal(new[] { "a1\n", "a2\n" }, text.Content);
        Assert.Equal("a1\na2", text.Compose(text.Content));
    }
}
