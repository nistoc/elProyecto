using TranslationImprover.Features.Refine.Infrastructure;
using Xunit;

namespace API.Tests;

/// <summary>
/// The built-in prompt is what a lesson is refined with when the operator set no prompt of their own
/// (agent05 shows it as the default of the Refiner tab). The pipeline fills two placeholders in it.
/// </summary>
public class BuiltInPromptTests
{
    private static int Count(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    [Fact]
    public void The_template_has_each_placeholder_once()
    {
        Assert.Equal(1, Count(FilePromptLoader.DefaultPrompt, "{context}"));
        Assert.Equal(1, Count(FilePromptLoader.DefaultPrompt, "{batch}"));
    }

    [Fact]
    public void The_template_names_the_line_format_and_the_same_number_of_lines()
    {
        Assert.Contains("- TIME SPEAKER: \"text\"", FilePromptLoader.DefaultPrompt);
        Assert.Contains("same number of lines", FilePromptLoader.DefaultPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_task_is_copying_with_one_exception_and_names_nothing_to_avoid()
    {
        // The wording that made the model look for things to fix in a transcript that is already right is gone.
        Assert.DoesNotContain("contains Russian speech where Spanish", FilePromptLoader.DefaultPrompt);
        Assert.Contains("Copy the lines of the CURRENT BATCH", FilePromptLoader.DefaultPrompt);
        Assert.Contains("Keep every word in the language and in the alphabet it already has", FilePromptLoader.DefaultPrompt);
        // A list of forbidden things with examples made a small model do those very things (measured): keep it out.
        Assert.DoesNotContain("never", FilePromptLoader.DefaultPrompt.Replace("never change it", ""), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("copy", RefinePipeline.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_prompt_file_gives_the_built_in_template()
    {
        var loader = new FilePromptLoader();
        Assert.Equal(FilePromptLoader.DefaultPrompt, await loader.LoadAsync(null, Path.GetTempPath()));
        Assert.Equal(FilePromptLoader.DefaultPrompt, await loader.LoadAsync("no_such_prompt_file.md", Path.GetTempPath()));
    }
}
