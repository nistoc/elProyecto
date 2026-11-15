namespace XtractManager.Features.Jobs.Application;

/// <summary>
/// What one refine run is asked with. The operator picks it when starting the run:
/// <list type="bullet">
/// <item><see cref="PromptLesson"/> — the run about the words in doubt. The refiner finds by code the words that
/// may be Spanish written in Cyrillic, or that mix letters of both alphabets, asks the model only in which
/// language each was said, and writes the spelling the transcript already has. The model returns no line. The
/// saved settings are not used: the prompt is the refiner's own, and so is the model (it has a default of its own
/// for this run).</item>
/// <item><see cref="PromptSaved"/> — the settings saved on the Refiner tab (they may hold a prompt for another
/// material): every line goes to the model block by block and is taken as the model wrote it. This is what a
/// run did before the choice existed.</item>
/// </list>
/// </summary>
public sealed record RefinerRunPlan(
    int BatchSize,
    int ContextLines,
    string? PromptFile,
    string? Model,
    string? SystemPrompt,
    bool CandidatesOnly)
{
    public const string PromptLesson = "lesson";
    public const string PromptSaved = "saved";

    public static RefinerRunPlan Choose(RefinerSettings settings, bool lessonPrompt) => lessonPrompt
        ? new RefinerRunPlan(
            IRefinerSettingsStore.DefaultBatchSize,
            IRefinerSettingsStore.DefaultContextLines,
            PromptFile: null,
            Model: null,
            SystemPrompt: null,
            CandidatesOnly: true)
        : new RefinerRunPlan(
            settings.BatchSize ?? IRefinerSettingsStore.DefaultBatchSize,
            settings.ContextLines ?? IRefinerSettingsStore.DefaultContextLines,
            settings.PromptTemplate != null ? IRefinerSettingsStore.PromptFileName : null,
            settings.Model,
            settings.SystemPrompt,
            CandidatesOnly: false);

    /// <summary>
    /// True for the lesson run, false for the saved settings (also when nothing is named: a client from before
    /// the choice), null for a word that names neither.
    /// </summary>
    public static bool? TryParsePrompt(string? prompt)
    {
        var word = prompt?.Trim();
        if (string.IsNullOrEmpty(word) || string.Equals(word, PromptSaved, StringComparison.OrdinalIgnoreCase))
            return false;
        return string.Equals(word, PromptLesson, StringComparison.OrdinalIgnoreCase) ? true : null;
    }
}
