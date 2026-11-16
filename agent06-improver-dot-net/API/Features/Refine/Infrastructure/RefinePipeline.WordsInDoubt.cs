using Microsoft.Extensions.Logging;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// The run that asks about the words in doubt only (<see cref="RefineJobRequest.CandidatesOnly"/>). The code finds
/// the words (<see cref="SpellingCandidates"/>), the judge says for each in which language it was said
/// (<see cref="ISpellingJudge"/>), the code writes the spelling (<see cref="CandidateRespelling"/>). Every request
/// is put to two models (<see cref="RefineJobRequestMapper.JudgesOf"/>), and a word is written anew only when
/// both name the same language for it. A request to the judge is to the job store and to the block files what a
/// block is to the other run: its lines before and after, the answers as they came, the tokens of all of them.
/// </summary>
public sealed partial class RefinePipeline
{
    private static readonly (CandidateRespelling.Left Why, string Text)[] WhyLeft =
    {
        (CandidateRespelling.Left.SaidInRussian, "said in Russian"),
        (CandidateRespelling.Left.OfBothLanguages, "made of both languages, or the judge could not tell"),
        (CandidateRespelling.Left.AnswersDiffer, "the models do not name the same language"),
        (CandidateRespelling.Left.NoSpanishNextTo, "a short word without Spanish on every side"),
        (CandidateRespelling.Left.NoSafeSpelling, "no spelling the code can vouch for"),
        (CandidateRespelling.Left.NoAnswer, "no answer of the judge"),
        (CandidateRespelling.Left.RequestFailed, "the request failed")
    };

    private async Task<(RefineRunTotals Totals, int Requests)> RunWordsInDoubtAsync(
        string jobId, RefineJobRequest request, TranscriptText source, string artifactRoot, CancellationToken cancellationToken)
    {
        if (_judge == null)
            throw new InvalidOperationException("The run asks about the words in doubt, and the service has no judge of them");

        var refineParent = jobId + ":refine";
        var lines = source.Content.ToList();
        var lexicon = SpanishLexicon.Of(lines);
        var candidates = SpellingCandidates.Find(lines, lexicon).Where(CandidateRespelling.IsAsked).ToList();
        var asks = CandidateRespelling.Plan(candidates);
        var cyrillicWords = CandidateRespelling.CyrillicWordsOf(lines);
        var judges = RefineJobRequestMapper.JudgesOf(request.Model);
        var words = new RefineRunRecord.WordsInDoubt(
            lines.Count,
            candidates.Select(c => c.Line).Distinct().Count(),
            candidates.Count(c => c.Kind == SpellingCandidates.Kind.BothAlphabets),
            candidates.Count(c => c.Kind == SpellingCandidates.Kind.SoundsSpanish),
            candidates.Count(c => c.Kind == SpellingCandidates.Kind.ShortInSpanish),
            asks.Count,
            judges);
        var promptPreview = $"[system]\n{_judge.SystemPrompt.Trim()}\n\n[words in doubt]\n{words.Summary}";

        _store.Update(jobId, new RefineJobStatusUpdate { TotalBatches = asks.Count });
        _nodeModel?.EnsureNode(refineParent, jobId, jobId, "phase");
        _nodeModel?.StartNode(refineParent);

        var record = new RefineRunRecord(request, artifactRoot, _logger);
        await record.WriteWordsStartAsync(request, _judge.SystemPrompt, words, cancellationToken);
        _store.Update(jobId, new RefineJobStatusUpdate { PromptPreview = promptPreview });
        var batchFiles = RefineBatchFiles.For(request, artifactRoot);
        if (batchFiles != null)
            await batchFiles.StartAsync(promptPreview, cancellationToken);

        var totals = new RefineRunTotals();
        var changes = new List<string>();
        var left = new Dictionary<CandidateRespelling.Left, int>();
        // The models are billed at different prices, so the record keeps the tokens of each apart.
        var tokens = judges.Select(_ => (In: 0, Out: 0)).ToArray();
        for (var i = 0; i < asks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ask = asks[i];
            var nodeId = refineParent + ":batch-" + i;
            _nodeModel?.EnsureNode(nodeId, refineParent, jobId, "batch");
            _nodeModel?.StartNode(nodeId);
            _store.Update(jobId, new RefineJobStatusUpdate { CurrentBatch = i + 1, CurrentPhase = $"Words in doubt {i + 1}/{asks.Count}" });
            _nodeModel?.UpdateNodeProgress(jobId, (i + 1) * 100 / asks.Count, $"Words in doubt {i + 1}/{asks.Count}");

            var before = JoinLines(ask.Lines.Select(l => lines[l]).ToList());
            // An answer that came is billed and kept, also when the request to the next model fails.
            var answers = new List<SpellingAnswers>(judges.Count);
            string? error = null;
            int written = 0, stayed = ask.Words.Count;
            try
            {
                var questions = CandidateRespelling.QuestionsOf(lines, ask);
                foreach (var model in judges)
                {
                    var answer = await _judge.AskAsync(questions, model, request.Temperature, request.OpenAIBaseUrl, cancellationToken);
                    tokens[answers.Count] = (tokens[answers.Count].In + answer.PromptTokens, tokens[answers.Count].Out + answer.CompletionTokens);
                    answers.Add(answer);
                }

                var said = CandidateRespelling.Agreed(answers.Select(a => a.Said).ToList(), out var differ);
                var applied = CandidateRespelling.Apply(lines, ask, said, cyrillicWords, differ);
                changes.AddRange(applied.Changes.Select(c => $"{c.At} «{c.Was}» → «{c.Now}»".TrimStart()));
                foreach (var (why, count) in applied.LeftAsTheyWere)
                    left[why] = left.GetValueOrDefault(why) + count;
                written = applied.Changes.Count;
                stayed = ask.Words.Count - written;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                error = ex.Message;
                left[CandidateRespelling.Left.RequestFailed] = left.GetValueOrDefault(CandidateRespelling.Left.RequestFailed) + ask.Words.Count;
                _logger?.LogWarning(ex, "Request {Index} about the words in doubt failed", i);
            }

            totals = totals with
            {
                FailedBatches = totals.FailedBatches + (error == null ? 0 : 1),
                PromptTokens = totals.PromptTokens + answers.Sum(a => a.PromptTokens),
                CompletionTokens = totals.CompletionTokens + answers.Sum(a => a.CompletionTokens),
                EditsLetThrough = totals.EditsLetThrough + written,
                EditsPutBack = totals.EditsPutBack + stayed
            };

            var delta = new RefineBatchDelta
            {
                Index = i + 1,
                Total = asks.Count,
                OriginalText = before,
                RefinedText = JoinLines(ask.Lines.Select(l => lines[l]).ToList()),
                Ok = error == null,
                Error = error,
                AskedWords = string.Join("\n", ask.Words.Select(w => w.Spanish == null ? w.Word : $"{w.Word} — {w.Spanish.Spelling}")),
                StartLine = ask.Lines[0] + 1,
                EndLine = ask.Lines[^1] + 1,
                RawText = string.Join("\n\n", answers.Select((a, n) => $"[{judges[n]}]\n{a.RawText}")),
                EditsLetThrough = written,
                EditsPutBack = stayed,
                PromptTokens = answers.Sum(a => a.PromptTokens),
                CompletionTokens = answers.Sum(a => a.CompletionTokens)
            };
            _store.Update(jobId, new RefineJobStatusUpdate { AppendBatch = delta, Totals = totals });
            if (batchFiles != null)
                await batchFiles.WriteAsync(delta, cancellationToken);

            _nodeModel?.CompleteNode(nodeId, error == null ? RefineJobState.Completed : RefineJobState.Failed, DateTimeOffset.UtcNow, error);
        }

        _nodeModel?.CompleteNode(refineParent, RefineJobState.Completed, DateTimeOffset.UtcNow);
        await WriteOutputAsync(request, source, lines, artifactRoot, cancellationToken);

        _logger?.LogInformation("Refine job {JobId}: {Words} words in doubt, {Written} written anew, {Stayed} left as they were", jobId, words.Words, totals.EditsLetThrough, totals.EditsPutBack);
        if (totals.FailedBatches > 0)
            _logger?.LogWarning("Refine job {JobId}: {Failed} of {Total} requests failed; their words are in the output as they were", jobId, totals.FailedBatches, asks.Count);
        await record.AppendWordsOutcomeAsync(words, totals,
            new RefineRunRecord.WordsOutcome(changes, WhyLeft.Select(w => (w.Text, left.GetValueOrDefault(w.Why))).ToList(),
                judges.Select((model, n) => (model, tokens[n].In, tokens[n].Out)).ToList()), cancellationToken);
        return (totals, asks.Count);
    }
}
