using TranslationImprover.Features.Refine.Domain;
using TranslationImprover.Features.Refine.Infrastructure;
using Xunit;

namespace API.Tests;

public class TranscriptParserTests
{
    [Fact]
    public void ParseStructure_NoMarkers_ReturnsAllAsContent()
    {
        var lines = new[] { "line1\n", "line2\n", "line3\n" };
        TranscriptParser.ParseStructure(lines, out var header, out var content, out var footer);

        Assert.Empty(header);
        Assert.Equal(3, content.Count);
        Assert.Equal("line1\n", content[0]);
        Assert.Equal("line3\n", content[2]);
        Assert.Empty(footer);
    }

    [Fact]
    public void ParseStructure_WithMarkers_SplitsHeaderContentFooter()
    {
        var lines = new[] { "header1\n", ">>>>>>>\n", "content1\n", "content2\n", "<<<<<\n", "footer1\n" };
        TranscriptParser.ParseStructure(lines, out var header, out var content, out var footer);

        Assert.Equal(2, header.Count);
        Assert.Equal("header1\n", header[0]);
        Assert.Equal(">>>>>>>\n", header[1]);
        Assert.Equal(2, content.Count);
        Assert.Equal("content1\n", content[0]);
        Assert.Equal("content2\n", content[1]);
        Assert.Equal(2, footer.Count);
        Assert.Equal("<<<<<\n", footer[0]);
        Assert.Equal("footer1\n", footer[1]);
    }

    [Fact]
    public void CreateBatches_Empty_ReturnsEmpty()
    {
        var batches = TranscriptParser.CreateBatches(Array.Empty<string>(), 5);
        Assert.Empty(batches);
    }

    [Fact]
    public void CreateBatches_ExactMultiple_SplitsEvenly()
    {
        var lines = new[] { "a\n", "b\n", "c\n", "d\n", "e\n", "f\n" };
        var batches = TranscriptParser.CreateBatches(lines, 2);

        Assert.Equal(3, batches.Count);
        Assert.Equal(0, batches[0].Index);
        Assert.Equal(2, batches[0].Lines.Count);
        Assert.Equal(1, batches[1].Index);
        Assert.Equal(2, batches[2].Index);
        Assert.Equal(4, batches[2].StartLine);
        Assert.Equal(6, batches[2].EndLine);
    }

    [Fact]
    public void CreateBatches_PartialLastBatch_LastBatchSmaller()
    {
        var lines = new[] { "a\n", "b\n", "c\n", "d\n", "e\n" };
        var batches = TranscriptParser.CreateBatches(lines, 2);

        Assert.Equal(3, batches.Count);
        Assert.Single(batches[2].Lines);
        Assert.Equal("e\n", batches[2].Lines[0]);
        Assert.Equal(4, batches[2].StartLine);
        Assert.Equal(5, batches[2].EndLine);
    }
}
