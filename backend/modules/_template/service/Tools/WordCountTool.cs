namespace Flowgent.Modules.Template.Tools;

using Flowgent.Module.Sdk;

public sealed record WordCountInput(string Text);

public sealed record WordCountOutput(int Words);

/// <summary><c>template.wordCount</c>: counts the whitespace-separated words of a text.</summary>
public sealed class WordCountTool : ModuleTool<WordCountInput, WordCountOutput>
{
    public const string Name = "template.wordCount";

    internal static int Count(string? text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    protected override Task<WordCountOutput> InvokeAsync(WordCountInput input, ToolInvocation invocation, CancellationToken ct) =>
        Task.FromResult(new WordCountOutput(Count(input.Text)));
}
