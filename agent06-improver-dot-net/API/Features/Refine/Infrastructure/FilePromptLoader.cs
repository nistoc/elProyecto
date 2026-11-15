using TranslationImprover.Features.Refine.Application;

namespace TranslationImprover.Features.Refine.Infrastructure;

public sealed class FilePromptLoader : IPromptLoader
{
    /// <summary>
    /// Built-in user prompt template (placeholders {context}, {batch}). The task is worded as copying with one
    /// rare exception (a Spanish word written in Cyrillic by ear is respelled), and it has no list of forbidden
    /// things. Both choices were measured on one lesson dialogue of 593 lines with gpt-4o-mini:
    /// the earlier wording ("the transcript contains Russian speech where Spanish was transcribed in Cyrillic",
    /// a numbered task to find and replace) changed 29 lines, most of them for the worse — Russian words
    /// translated into Spanish, the speaker's own Spanish corrected; a wording with "never translate, never write
    /// a Russian word in Latin letters" and examples of each changed 47 — the model did the very things the
    /// examples named; this wording changed 7. It still errs in a few of them, so the output is to be read.
    /// </summary>
    public const string DefaultPrompt = @"These are lines from the transcript of a Spanish lesson taught in Russian. Copy the lines of the CURRENT BATCH to the output.

The transcript is bilingual on purpose: Russian is written in Cyrillic and Spanish in Latin letters, often inside one line. Keep every word in the language and in the alphabet it already has. Keep the speaker's wording as it is, mistakes included.

One kind of word is respelled while copying: a word in Cyrillic letters that is not a Russian word but a Spanish word written by ear, such as 'вале' (vale), 'пор фавор' (por favor), 'ме густа' (me gusta). Write such a word in Spanish spelling. Such words are rare: most batches have none and are copied without any change.

Line format: - TIME SPEAKER: ""text"" (SPEAKER is a label such as speaker_0 or a participant's name; never change it).
Return the EXACT same number of lines as the current batch, in the same order.

CONTEXT (for understanding only, do not return it):
{context}

CURRENT BATCH:
{batch}

Return ONLY the lines of the current batch (same count as input), no explanations, no markdown code blocks.";

    public Task<string> LoadAsync(string? promptFileRelativePath, string workspaceRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(promptFileRelativePath))
            return Task.FromResult(DefaultPrompt);

        var path = Path.Combine(workspaceRoot, promptFileRelativePath.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!File.Exists(path))
            return Task.FromResult(DefaultPrompt);

        try
        {
            var content = File.ReadAllText(path, System.Text.Encoding.UTF8);
            return Task.FromResult(string.IsNullOrWhiteSpace(content) ? DefaultPrompt : content.Trim());
        }
        catch
        {
            return Task.FromResult(DefaultPrompt);
        }
    }
}
