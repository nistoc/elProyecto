using System.Text.Json;
using TranslationImprover.Features.Refine.Domain;
using TranslationImprover.Features.Refine.Infrastructure;
using Xunit;

namespace API.Tests;

/// <summary>What is read out of the model's answer: its text, the tokens billed, and the lines of a block.</summary>
public class OpenAIAnswerTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static readonly BatchInfo Block = new()
    {
        Index = 3,
        Lines = new[] { "- 1.00 Olga: \"вале\"\n", "- 2.00 Maria: \"Да.\"\n" },
        Context = new[] { "- 0.50 Maria: \"Hola.\"\n" }
    };

    [Fact]
    public void The_text_and_the_usage_are_read()
    {
        var answer = OpenAIChatClient.ReadAnswer(Json(
            "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"- 1.00 Olga: \\\"vale\\\"\"}}],\"usage\":{\"prompt_tokens\":321,\"completion_tokens\":45,\"total_tokens\":366}}"));

        Assert.True(answer.HasText);
        Assert.Equal("- 1.00 Olga: \"vale\"", answer.Content);
        Assert.Equal(321, answer.PromptTokens);
        Assert.Equal(45, answer.CompletionTokens);
    }

    [Fact]
    public void An_answer_without_usage_counts_no_tokens()
    {
        var answer = OpenAIChatClient.ReadAnswer(Json("{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));

        Assert.True(answer.HasText);
        Assert.Equal(0, answer.PromptTokens);
        Assert.Equal(0, answer.CompletionTokens);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":null,\"refusal\":\"no\"}}]}")]
    [InlineData("{\"error\":{\"message\":\"overloaded\"}}")]
    [InlineData("[]")]
    public void An_answer_of_another_shape_has_no_text(string json)
    {
        Assert.False(OpenAIChatClient.ReadAnswer(Json(json)).HasText);
    }

    [Fact]
    public void The_lines_of_a_block_are_the_answers_lines_without_the_empty_ones()
    {
        var result = OpenAIRefineClient.ToResult(
            new OpenAIChatClient.Answer("\n- 1.00 Olga: \"vale\"  \r\n\n- 2.00 Maria: \"Да.\"\n\n", 10, 4), Block);

        Assert.True(result.Success);
        Assert.Equal(3, result.BatchIndex);
        Assert.Equal(new[] { "- 1.00 Olga: \"vale\"\n", "- 2.00 Maria: \"Да.\"\n" }, result.FixedLines);
        Assert.Equal(10, result.PromptTokens);
        Assert.Equal(4, result.CompletionTokens);
        // The answer is kept as it came, so what a check changes later can be told from what the model wrote.
        Assert.Contains("- 1.00 Olga: \"vale\"  \r\n", result.RawAnswer);
    }

    [Fact]
    public void An_answer_with_no_line_gives_the_block_back()
    {
        var result = OpenAIRefineClient.ToResult(new OpenAIChatClient.Answer("  \n ", 10, 0), Block);

        Assert.True(result.Success);
        Assert.Equal(Block.Lines, result.FixedLines);
    }

    [Fact]
    public void The_prompt_holds_the_block_and_its_context()
    {
        var prompt = OpenAIRefineClient.BuildUserPrompt(Block, "C: {context}\nB:\n{batch}END");

        Assert.Equal(
            "C: Context from previous batch (for continuity):\n```\n- 0.50 Maria: \"Hola.\"\n```\nB:\n- 1.00 Olga: \"вале\"\n- 2.00 Maria: \"Да.\"\nEND",
            prompt);
        Assert.Contains("No previous context available.",
            OpenAIRefineClient.BuildUserPrompt(new BatchInfo { Lines = Block.Lines }, "{context}"));
    }
}
