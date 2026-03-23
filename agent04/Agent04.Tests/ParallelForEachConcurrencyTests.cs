using Xunit;

namespace Agent04.Tests;

/// <summary>
/// Documents the BCL contract we rely on: sliding-window parallelism with a cap (same primitive as TranscriptionPipeline).
/// </summary>
public sealed class ParallelForEachConcurrencyTests
{
    [Fact]
    public async Task ParallelForEachAsync_never_exceeds_max_degree_for_async_work()
    {
        const int parallel = 3;
        var inFlight = 0;
        var maxObserved = 0;
        var indices = Enumerable.Range(0, 12).ToList();

        await Parallel.ForEachAsync(
            indices,
            new ParallelOptions { MaxDegreeOfParallelism = parallel },
            async (_, ct) =>
            {
                var n = Interlocked.Increment(ref inFlight);
                int old;
                do
                {
                    old = maxObserved;
                } while (n > old && Interlocked.CompareExchange(ref maxObserved, n, old) != old);

                await Task.Delay(15, ct);
                Interlocked.Decrement(ref inFlight);
            });

        Assert.InRange(maxObserved, 1, parallel);
    }
}
