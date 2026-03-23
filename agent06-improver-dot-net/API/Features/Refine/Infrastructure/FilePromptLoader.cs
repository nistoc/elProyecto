using TranslationImprover.Features.Refine.Application;

namespace TranslationImprover.Features.Refine.Infrastructure;

public sealed class FilePromptLoader : IPromptLoader
{
    private const string DefaultPrompt = @"You are fixing a Russian-Spanish language learning transcript.

The transcript contains Russian speech where Spanish words/phrases were transcribed phonetically in Cyrillic.

Your task:
1. Find Spanish words written in Cyrillic (e.g. 'вале' should be 'vale', 'пор фавор' should be 'por favor')
2. Replace them with correct Spanish spelling in Latin script
3. Keep everything else EXACTLY as is (timestamps, speaker labels, Russian text)
4. Preserve ALL formatting and line structure

IMPORTANT:
- Only fix Spanish words that are clearly Spanish (not Russian words)
- Keep the line format: - TIME speaker_N: ""text""
- Do NOT translate, only transliterate Cyrillic Spanish back to Latin
- Return the EXACT same number of lines as input

CONTEXT:
{context}

CURRENT BATCH:
{batch}

Return ONLY the fixed lines (same count as input), no explanations, no markdown code blocks.";

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
