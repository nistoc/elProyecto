using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Infrastructure;
using Xunit;

namespace API.Tests;

/// <summary>What is sent to the judge of the words in doubt, and what is read from its answer.</summary>
public class OpenAISpellingJudgeTests
{
    private static readonly SpellingQuestion[] Questions =
    {
        new(1, "вале", "Да, [[вале]], это мой дом."),
        new(2, "тема", "Это другая [[тема]]."),
        new(3, "Теngo", "[[Теngo]] una casa."),
    };

    [Fact]
    public void The_request_is_the_words_with_their_lines_and_nothing_else()
    {
        var prompt = OpenAISpellingJudge.BuildUserPrompt(Questions);

        Assert.Equal(
            "{\"items\":[{\"id\":1,\"word\":\"вале\",\"text\":\"Да, [[вале]], это мой дом.\"},"
            + "{\"id\":2,\"word\":\"тема\",\"text\":\"Это другая [[тема]].\"},"
            + "{\"id\":3,\"word\":\"Теngo\",\"text\":\"[[Теngo]] una casa.\"}]}",
            prompt);
    }

    [Fact]
    public void An_answer_is_read_by_the_number_of_its_word()
    {
        var said = OpenAISpellingJudge.ReadAnswers(
            "{\"answers\":[{\"id\":1,\"word\":\"вале\",\"russian\":false,\"said\":\"es\"},"
            + "{\"id\":\"2\",\"word\":\"тема\",\"russian\":true,\"said\":\"RU\"},"
            + "{\"id\":3,\"word\":\"Теngo\",\"russian\":false,\"said\":\"mix\"}]}",
            Questions);

        Assert.Equal(SaidIn.Spanish, said[1]);
        Assert.Equal(SaidIn.Russian, said[2]);
        Assert.Equal(SaidIn.Both, said[3]);
    }

    [Fact]
    public void An_answer_that_repeats_another_word_is_dropped()
    {
        // The model slipped by one: under the number of «тема» it answers about «вале».
        var said = OpenAISpellingJudge.ReadAnswers(
            "{\"answers\":[{\"id\":1,\"word\":\"вале\",\"russian\":false,\"said\":\"es\"},"
            + "{\"id\":2,\"word\":\"вале\",\"russian\":false,\"said\":\"es\"}]}",
            Questions);

        Assert.Equal(new[] { 1 }, said.Keys);
    }

    [Fact]
    public void What_the_model_says_about_the_word_being_Russian_is_not_read()
    {
        // The word about Russian is a step of the model on its way to the answer; the answer is what counts.
        var said = OpenAISpellingJudge.ReadAnswers(
            "{\"answers\":[{\"id\":1,\"said\":\"es\"},{\"id\":2,\"word\":\"тема\",\"russian\":true,\"said\":\"es\"}]}", Questions);

        Assert.Equal(SaidIn.Spanish, said[1]);
        Assert.Equal(SaidIn.Spanish, said[2]);
    }

    [Theory]
    [InlineData("I cannot answer that.")]
    [InlineData("{\"answers\":\"none\"}")]
    [InlineData("{\"answers\":[{\"id\":7,\"said\":\"es\"},{\"said\":\"es\"},{\"id\":1,\"said\":\"maybe\"},{\"id\":2}]}")]
    [InlineData("{\"answers\":[{\"id\":1,")]
    public void What_cannot_be_read_gives_no_answer(string content)
    {
        Assert.Empty(OpenAISpellingJudge.ReadAnswers(content, Questions));
    }

    [Fact]
    public void The_answer_may_have_text_around_the_JSON()
    {
        var said = OpenAISpellingJudge.ReadAnswers("```json\n{\"answers\":[{\"id\":2,\"word\":\"тема\",\"russian\":true,\"said\":\"ru\"}]}\n```", Questions);

        Assert.Equal(SaidIn.Russian, said[2]);
    }
}
