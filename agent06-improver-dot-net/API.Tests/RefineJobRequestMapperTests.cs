using System.Collections;
using System.Reflection;
using Google.Protobuf.Reflection;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Proto;
using TranslationImprover.Services;
using Xunit;

namespace API.Tests;

/// <summary>
/// A request reaches the pipeline whole, whichever entry brought it. The two entries used to copy the request
/// field by field on their own; a field added to one copy and not to the other was lost without an error.
/// These tests fill EVERY property by reflection, so a field added later and forgotten in the mapper fails here.
/// </summary>
public class RefineJobRequestMapperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "refine-mapper-" + Guid.NewGuid().ToString("N"));

    public RefineJobRequestMapperTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static readonly PropertyInfo[] RequestProperties =
        typeof(RefineJobRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    /// <summary>A value of the property's type that is not its default and not what a fresh request holds.</summary>
    private static object ValueFor(PropertyInfo property, int n)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(string)) return "value" + n;
        if (type == typeof(int)) return 40 + n;
        if (type == typeof(float)) return 0.25f + n;
        // A fresh request has SaveIntermediate on, every other flag off.
        if (type == typeof(bool)) return !(bool)property.GetValue(new RefineJobRequest())!;
        if (type == typeof(IReadOnlyList<string>)) return new List<string> { "tag" + n };
        throw new InvalidOperationException($"No test value for {property.Name} of type {type.Name}: teach this test the new type");
    }

    private static void AssertSame(object? expected, object? actual, string name)
    {
        if (expected is IEnumerable expectedList and not string)
            Assert.Equal(expectedList.Cast<object>(), ((IEnumerable)actual!).Cast<object>());
        else
            Assert.True(Equals(expected, actual), $"{name}: expected {expected}, got {actual}");
    }

    [Fact]
    public void Normalize_KeepsEveryPropertyOfTheRequest()
    {
        var request = new RefineJobRequest();
        var n = 0;
        foreach (var property in RequestProperties)
            property.SetValue(request, ValueFor(property, ++n));

        var normalized = RefineJobRequestMapper.Normalize(request);

        foreach (var property in RequestProperties)
            AssertSame(property.GetValue(request), property.GetValue(normalized), property.Name);
    }

    [Fact]
    public void FromProto_PassesEveryFieldOfTheMessage()
    {
        var message = new SubmitRefineJobRequest();
        var n = 0;
        foreach (var field in SubmitRefineJobRequest.Descriptor.Fields.InDeclarationOrder())
        {
            n++;
            if (field.IsRepeated)
            {
                ((IList)field.Accessor.GetValue(message)).Add("tag" + n);
                continue;
            }

            field.Accessor.SetValue(message, field.FieldType switch
            {
                FieldType.String => "value" + n,
                FieldType.Int32 => 40 + n,
                FieldType.Float => 0.25f + n,
                FieldType.Bool => true,
                _ => throw new InvalidOperationException($"No test value for the field {field.Name}: teach this test the new type")
            });
        }

        var request = RefineJobRequestMapper.Normalize(RefinerGrpcService.FromProto(message));

        // Every property of the request has a field of the message behind it, and none was left at its default.
        Assert.Equal(SubmitRefineJobRequest.Descriptor.Fields.InDeclarationOrder().Count, RequestProperties.Length);
        var untouched = new RefineJobRequest { SaveIntermediate = false };
        foreach (var property in RequestProperties)
        {
            var value = property.GetValue(request);
            Assert.True(value != null && !Equals(value, property.GetValue(untouched)), $"{property.Name} did not come from the message");
        }

        Assert.Equal(message.SystemPrompt, request.SystemPrompt);
        Assert.Equal(message.SourceName, request.SourceName);
        Assert.Equal(message.OpenaiBaseUrl, request.OpenAIBaseUrl);
        Assert.Equal(message.IntermediateDir, request.IntermediateDir);
        Assert.Equal(message.Tags.ToList(), request.Tags);
    }

    [Fact]
    public void Normalize_GivesDefaultsAndDropsEmptyValues()
    {
        var normalized = RefineJobRequestMapper.Normalize(new RefineJobRequest
        {
            InputFilePath = "  transcript.md  ",
            InputContent = "",
            OutputFilePath = "   ",
            BatchSize = 0,
            ContextLines = -1,
            Model = " ",
            SystemPrompt = "  Copy the lines.  ",
            SourceName = "\t",
            CallbackUrl = "",
            Tags = Array.Empty<string>(),
        });

        Assert.Equal("transcript.md", normalized.InputFilePath);
        Assert.Null(normalized.InputContent);
        Assert.Null(normalized.OutputFilePath);
        Assert.Equal(RefineJobRequestMapper.DefaultBatchSize, normalized.BatchSize);
        Assert.Equal(RefineJobRequestMapper.DefaultContextLines, normalized.ContextLines);
        Assert.Equal(RefineJobRequestMapper.DefaultModel, normalized.Model);
        Assert.Equal("Copy the lines.", normalized.SystemPrompt);
        Assert.Null(normalized.SourceName);
        Assert.Null(normalized.CallbackUrl);
        Assert.Null(normalized.Tags);
        Assert.Equal(0, RefineJobRequestMapper.Normalize(new RefineJobRequest { ContextLines = 0 }).ContextLines);
    }

    [Fact]
    public void A_run_about_the_words_in_doubt_has_a_default_model_of_its_own()
    {
        Assert.Equal(RefineJobRequestMapper.DefaultJudgeModel,
            RefineJobRequestMapper.Normalize(new RefineJobRequest { CandidatesOnly = true }).Model);
        Assert.Equal("gpt-4o-mini",
            RefineJobRequestMapper.Normalize(new RefineJobRequest { CandidatesOnly = true, Model = " gpt-4o-mini " }).Model);
        Assert.Equal(RefineJobRequestMapper.DefaultModel,
            RefineJobRequestMapper.Normalize(new RefineJobRequest { RespellingOnly = true }).Model);
    }

    [Fact]
    public void Validate_NamesWhatIsWrong()
    {
        File.WriteAllText(Path.Combine(_root, "there.md"), "- 1.00 Olga: \"vale\"\n");
        RefineRequestError? Check(RefineJobRequest r) => RefineJobRequestMapper.Validate(RefineJobRequestMapper.Normalize(r), _root);

        Assert.Null(Check(new RefineJobRequest { InputFilePath = "there.md", OutputFilePath = "out.md" }));
        Assert.Null(Check(new RefineJobRequest { InputContent = "- 1.00 Olga: \"vale\"\n" }));

        Assert.Contains("must be set", Check(new RefineJobRequest { InputFilePath = "   " })!.Message);

        var missing = Check(new RefineJobRequest { InputFilePath = "absent.md" })!;
        Assert.Equal("Input file not found", missing.Message);
        Assert.Equal("inputFilePath", missing.Field);
        Assert.Equal("absent.md", missing.Value);

        var rooted = Check(new RefineJobRequest { InputFilePath = Path.Combine(_root, "there.md") })!;
        Assert.Contains("must be relative", rooted.Message);
        Assert.Equal("inputFilePath", rooted.Field);

        var rootedOutput = Check(new RefineJobRequest { InputFilePath = "there.md", OutputFilePath = Path.Combine(_root, "out.md") })!;
        Assert.Contains("output_file_path must be relative", rootedOutput.Message);
        Assert.Equal("outputFilePath", rootedOutput.Field);
    }
}
