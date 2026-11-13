using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public sealed class SpeechTimeMapTests
{
    // Source 0..60 s: speech 0–10, silence 10–30, speech 30–40, silence 40–55, speech 55–60.
    private static readonly SilenceInterval[] Silence =
    {
        new(10, 30, 20),
        new(40, 55, 15),
    };

    private static SpeechTimeMap MapWithGap(double keep = 0.2) =>
        new(SpeechTimeMap.Plan(Silence, 60, keep, 0.02).Where(s => !s.IsGap).Select(s => s.Piece!));

    [Fact]
    public void Plan_lays_pieces_and_gaps_in_file_order()
    {
        var steps = SpeechTimeMap.Plan(Silence, 60, 0.2, 0.02);

        Assert.Equal(new[] { false, true, false, true, false }, steps.Select(s => s.IsGap));
        var pieces = steps.Where(s => !s.IsGap).Select(s => s.Piece!).ToList();
        Assert.Equal(new SpeechPiece(0, 10, 0), pieces[0]);
        Assert.Equal(30, pieces[1].SourceStartSec);
        Assert.Equal(10.2, pieces[1].OutputStartSec, 6);
        Assert.Equal(55, pieces[2].SourceStartSec);
        Assert.Equal(60, pieces[2].SourceEndSec);
        Assert.Equal(20.4, pieces[2].OutputStartSec, 6);
        Assert.Equal(25.4, pieces[2].OutputEndSec, 6);
    }

    [Fact]
    public void Plan_without_kept_silence_has_no_gaps()
    {
        var steps = SpeechTimeMap.Plan(Silence, 60, 0, 0.02);

        Assert.DoesNotContain(steps, s => s.IsGap);
        Assert.Equal(new[] { 0.0, 10.0, 20.0 }, steps.Select(s => s.Piece!.OutputStartSec));
    }

    [Fact]
    public void Plan_starts_with_a_gap_when_the_recording_starts_with_silence_and_skips_tiny_pieces()
    {
        // silence 0–5, a 0.01 s blip, silence 5.01–20, speech to the end
        var steps = SpeechTimeMap.Plan(
            new[] { new SilenceInterval(0, 5, 5), new SilenceInterval(5.01, 20, 14.99) }, 30, 0.2, 0.02);

        Assert.Equal(new[] { true, true, false }, steps.Select(s => s.IsGap));
        var piece = steps[2].Piece!;
        Assert.Equal(20, piece.SourceStartSec);
        Assert.Equal(0.4, piece.OutputStartSec, 6);
    }

    [Theory]
    [InlineData(0.0, 0.0)]      // inside the first piece
    [InlineData(4.5, 4.5)]
    [InlineData(10.1, 30.0)]    // in the gap: sound begins again at the next piece
    [InlineData(10.2, 30.0)]    // start of the second piece
    [InlineData(15.2, 35.0)]
    [InlineData(20.3, 55.0)]    // second gap
    [InlineData(22.4, 57.0)]
    [InlineData(99.0, 60.0)]    // past the end
    public void ToSourceTime_translates_compressed_time_to_the_recording(double output, double source)
    {
        Assert.Equal(source, MapWithGap().ToSourceTime(output), 6);
    }

    [Fact]
    public void ToSourceTime_before_the_first_piece_is_its_start()
    {
        var map = new SpeechTimeMap(new[] { new SpeechPiece(5, 9, 0.2) });

        Assert.Equal(5, map.ToSourceTime(0.05), 6);
    }

    [Fact]
    public void ToSourceTime_of_an_empty_map_changes_nothing()
    {
        Assert.Equal(12.5, new SpeechTimeMap(Array.Empty<SpeechPiece>()).ToSourceTime(12.5));
    }

    [Fact]
    public void Segment_that_starts_a_moment_early_belongs_to_the_next_piece()
    {
        var map = MapWithGap();

        // The model put the phrase 0.2 s before the cut; it lasts into the second piece.
        Assert.Equal(30.0, map.ToSourceSegmentStart(9.8, 12.0), 6);
        // A phrase that ends inside the first piece stays where it is.
        Assert.Equal(9.8, map.ToSourceSegmentStart(9.8, 10.0), 6);
        // A phrase that began well before the cut keeps its own start even when it runs past it.
        Assert.Equal(9.0, map.ToSourceSegmentStart(9.0, 12.0), 6);
        // Start in the gap or in the next piece: plain translation.
        Assert.Equal(30.0, map.ToSourceSegmentStart(10.1, 12.0), 6);
        Assert.Equal(31.8, map.ToSourceSegmentStart(12.0, 13.0), 6);
    }

    [Fact]
    public async Task Map_file_round_trip_and_delete()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent04_speech_map_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(await SpeechTimeMapFile.TryLoadAsync(root));

            var pieces = MapWithGap().Pieces;
            await SpeechTimeMapFile.SaveAsync(root, new SilenceCompressionReport
            {
                InputPath = "in.wav",
                OutputPath = "in_silc.wav",
                InputDurationSec = 60,
                OutputDurationSec = 25.4,
                KeepSilenceSec = 0.2,
                AppliedCompression = true,
                SpeechPieces = pieces
            });

            Assert.True(File.Exists(Path.Combine(root, "timeline", "speech_map.json")));
            var loaded = await SpeechTimeMapFile.TryLoadAsync(root);
            Assert.NotNull(loaded);
            Assert.Equal(pieces, loaded!.Pieces);

            SpeechTimeMapFile.Delete(root);
            Assert.Null(await SpeechTimeMapFile.TryLoadAsync(root));
            SpeechTimeMapFile.Delete(root); // nothing to remove is fine
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Writer_prints_recording_times_when_given_the_map()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agent04_speech_map_md_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var writer = new TranscriptionOutputWriter();
            // A chunk cut from the compressed file at 10.0 s; times below are chunk-local.
            var segments = new[]
            {
                new ASRSegment(0.5, 1.5, "after the first pause", "A"),   // 10.5 → piece 2 → 30.3
                new ASRSegment(10.6, 12.0, "after the second pause", "A"), // 20.6 → piece 3 → 55.2
            };

            writer.WritePerChunkMarkdown("job_part_001.wav", segments, 10.0, 0, dir, MapWithGap());
            writer.WritePerChunkMarkdown("job_part_001_plain.wav", segments, 10.0, 0, dir);

            Assert.Equal(
                ">>>>>>>\n- 30.30 speaker_0: \"after the first pause\"\n- 55.20 speaker_0: \"after the second pause\"\n<<<<<\n",
                File.ReadAllText(Path.Combine(dir, "job_part_001.md")));
            Assert.Equal(
                ">>>>>>>\n- 10.50 speaker_0: \"after the first pause\"\n- 20.60 speaker_0: \"after the second pause\"\n<<<<<\n",
                File.ReadAllText(Path.Combine(dir, "job_part_001_plain.md")));

            // A chunk without time marks stands at the start of its chunk, translated too.
            var combined = Path.Combine(dir, "combined.md");
            writer.InitializeMarkdown(combined);
            writer.AppendSegmentsToMarkdown(combined, new[] { new ASRSegment(0, 0, "whole chunk", null) }, 10.2, 0, MapWithGap());
            writer.FinalizeMarkdown(combined);
            Assert.Contains("- 30.00 speaker_0: \"whole chunk\"", File.ReadAllText(combined));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best-effort */ }
        }
    }
}
