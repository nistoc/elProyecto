namespace XtractManager.Features.Jobs.Application;

/// <summary>
/// What one refine run is asked with. The operator picks the prompt when starting the run:
/// <list type="bullet">
/// <item><see cref="PromptLesson"/> — the refiner's built-in lesson prompt with its measured block size
/// (<see cref="IRefinerSettingsStore.DefaultBatchSize"/> lines, <see cref="IRefinerSettingsStore.DefaultContextLines"/>
/// of context), the default model, and the check of edits: of what the model changes only a Spanish word
/// written in Cyrillic and respelled in Latin letters is kept. The saved settings are not used.</item>
/// <item><see cref="PromptSaved"/> — the settings saved on the Refiner tab (they may hold a prompt for another
/// material), the lines taken as the model wrote them. This is what a run did before the choice existed.</item>
/// </list>
/// </summary>
public sealed record RefinerRunPlan(
    int BatchSize,
    int ContextLines,
    string? PromptFile,
    string? Model,
    string? SystemPrompt,
    bool RespellingOnly)
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
            RespellingOnly: true)
        : new RefinerRunPlan(
            settings.BatchSize ?? IRefinerSettingsStore.DefaultBatchSize,
            settings.ContextLines ?? IRefinerSettingsStore.DefaultContextLines,
            settings.PromptTemplate != null ? IRefinerSettingsStore.PromptFileName : null,
            settings.Model,
            settings.SystemPrompt,
            RespellingOnly: false);

    /// <summary>
    /// True for the lesson prompt, false for the saved settings (also when nothing is named: a client from before
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
