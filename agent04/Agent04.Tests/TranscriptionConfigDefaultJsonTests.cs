using Agent04.Features.Transcription.Domain;
using Xunit;

namespace Agent04.Tests;

/// <summary>
/// Ensures repo <c>Agent04/config/default.json</c> is loadable and numeric keys used by the pipeline
/// deserialize correctly (JSON numbers are stored as <see cref="double"/> in <see cref="TranscriptionConfig"/>).
/// </summary>
public sealed class TranscriptionConfigDefaultJsonTests
{
    [Fact]
    public async Task DefaultJson_loads_parallel_transcription_workers_as_6()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config", "default.json");
        Assert.True(File.Exists(path), $"Expected bundled config at {path}");

        var config = await TranscriptionConfig.FromFileAsync(path);

        Assert.Equal(6, config.Get<int?>("parallel_transcription_workers"));

        Assert.True(config.Get<bool?>("pre_split") == true);
        Assert.Equal(5, config.Get<double?>("target_chunk_mb"));
        Assert.Equal("split_chunks", config.SplitChunksDir);
    }
}
