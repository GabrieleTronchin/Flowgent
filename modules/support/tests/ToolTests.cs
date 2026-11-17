namespace Flowgent.Modules.Support.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;
using Flowgent.Mcp.Support;

/// <summary>
/// The pure locate algorithm (<see cref="LocateComponentCore"/>) against the real dummy apps. These assertions are the
/// direct-helper parity for <c>support.locateComponent</c>: the tool now runs in the standalone MCP server, but the
/// algorithm is single-sourced in the core, so the located path/component/language/snippet values are unchanged.
/// </summary>
public class ToolTests
{
    private static readonly string DummyAppsRoot = Path.Combine(SupportPlatform.ModuleFolder, "dummy-apps");

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("scenario-a1", "application-a", "OrderController.cs", "dummy-apps/application-a/src/Controllers/OrderController.cs", "csharp")]
    [InlineData("scenario-a2", "application-a", "OrderSummary.razor", "dummy-apps/application-a/src/Components/OrderSummary.razor", "razor")]
    [InlineData("scenario-a3", "application-a", "OrderServiceTests.cs", "dummy-apps/application-a/tests/OrderServiceTests.cs", "csharp")]
    [InlineData("scenario-b1", "application-b", "UserController.cs", "dummy-apps/application-b/src/Controllers/UserController.cs", "csharp")]
    [InlineData("scenario-b2", "application-b", "UserProfile.razor", "dummy-apps/application-b/src/Components/UserProfile.razor", "razor")]
    [InlineData("scenario-b3", "application-b", "UserServiceTests.cs", "dummy-apps/application-b/tests/UserServiceTests.cs", "csharp")]
    public void Scenario_LocatesTheAffectedFile(string sampleId, string folder, string component, string path, string language)
    {
        var input = SupportPlatform.Sample(sampleId).Input!;

        var result = Locate(folder, $"{input["subject"]} {input["body"]}");

        Assert.True(result.Found);
        Assert.Equal(component, result.Component);
        Assert.Equal(path, result.Path);
        Assert.Equal(language, result.Language);
        Assert.Equal(File.ReadAllText(Path.Combine(SupportPlatform.ModuleFolder, path)), result.Snippet);
    }

    [Fact]
    public void EarliestMention_Wins()
    {
        var result = Locate("application-a", "OrderSummary looks wrong, maybe because of OrderController");

        Assert.Equal("OrderSummary.razor", result.Component);
    }

    [Fact]
    public void NoMention_ReturnsUnknownComponentUnderTheApplication()
    {
        var result = Locate("application-b", "Something is slow");

        Assert.False(result.Found);
        Assert.Equal("UnknownComponent", result.Component);
        Assert.Equal("dummy-apps/application-b/src/UnknownComponent", result.Path);
        Assert.Null(result.Snippet);
    }

    [Theory]
    [InlineData("../application-a")]
    [InlineData("..")]
    [InlineData("application-a/../../definition")]
    [InlineData("C:")]
    [InlineData("")]
    [InlineData("Application-A")]
    public void InvalidFolders_AreRejected(string folder)
    {
        var ex = Assert.Throws<LocateComponentException>(() => Locate(folder, "OrderController"));

        Assert.Contains("application folder", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownFolder_IsRejected()
    {
        var ex = Assert.Throws<LocateComponentException>(() => Locate("application-z", "OrderController"));

        Assert.Equal("Unknown application folder 'application-z'.", ex.Message);
    }

    [Fact]
    public void Output_MatchesTheDeclaredToolSchema()
    {
        var result = Locate("application-a", "OrderController");
        var output = JsonNode.Parse(JsonSerializer.Serialize(result, Web))!;
        var schema = SupportPlatform.Definition.GetSchema(SupportPlatform.Definition.FindTool("support.locateComponent")!.Output!.Schema);

        Assert.Empty(Flowgent.Definitions.Schemas.JsonSchemaValidator.Validate(schema, output));
    }

    internal static LocateResult Locate(string folder, string text) =>
        LocateComponentCore.LocateAsync(folder, text, DummyAppsRoot, "dummy-apps", CancellationToken.None).GetAwaiter().GetResult();
}
