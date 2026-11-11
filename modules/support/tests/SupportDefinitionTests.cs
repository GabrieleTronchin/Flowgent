namespace Flowgent.Modules.Support.Tests;

using System.Text.Json.Nodes;
using Flowgent.Runtime.Execution;
using Flowgent.Runtime.Llm;

/// <summary>
/// Unit tests of the support workflow's Demo-mode behaviour and agent mapping, ported from the pre-migration
/// SupportDefinitionTests (FEAT-002 had disabled this file with <c>#if false</c> because it was built on the deleted
/// custom step model). The behaviour that used to live in the workflow <c>demo:</c> blocks and <c>rule</c>/<c>map</c>
/// steps now lives in <see cref="DemoResponseProvider"/> (classification keyword rules, severity, pull-request paths)
/// and in the declarative support YAML (routing, category→role mapping); the routing single/both/no-application and
/// category→agent outcomes are asserted end-to-end by <see cref="SupportParityTests"/>, and reproduced here at the
/// unit level where the logic is pure.
/// </summary>
public class SupportDefinitionTests
{
    private static readonly DemoResponseProvider Demo = new();

    private static readonly string[] Categories = ["BackendBug", "FrontendBug", "QualityTestIssue"];
    private static readonly Dictionary<string, string> Roles = new()
    {
        ["BackendBug"] = "BackendDeveloper",
        ["FrontendBug"] = "FrontendDeveloper",
        ["QualityTestIssue"] = "QAEngineer",
    };

    [Fact]
    public void Definition_IsValidWithoutWarnings()
    {
        var module = SupportPlatform.Definition;

        Assert.Equal("support", module.Id);
        Assert.Empty(module.Warnings);
        Assert.Equal(6, module.Manifest.Agents.Count);
        Assert.Equal("dummy-apps", module.Config["dummyAppsRoot"]!.GetValue<string>());
        Assert.Equal(10, module.Samples.Count);
    }

    // ── Demo classification (keyword rules, ported from demo.rules) ─────────────────────

    [Theory]
    [InlineData("NullReferenceException in OrderController - Application A", "The API crashes", "BackendBug")]
    [InlineData("Blank total price in OrderSummary component", "The .razor component renders blank", "FrontendBug")]
    [InlineData("Flaky test due to hardcoded date", "The test fails every other day", "QualityTestIssue")]
    [InlineData("How do I reset my password?", "I forgot my password.", "OutOfScope")]
    public void DemoClassification_FollowsTheKeywordRules(string subject, string body, string expected)
    {
        var output = Classify(subject, body);

        Assert.Equal(expected, output["category"]!.GetValue<string>());
        Assert.Equal(expected != "OutOfScope", output["isCodeRelated"]!.GetValue<bool>());
    }

    [Fact]
    public void DemoClassification_IsConsistent()
    {
        var output = Classify("NullReferenceException in the API", "the endpoint crashes");

        // isCodeRelated is true exactly when the category is not OutOfScope (the classification.schema.json contract).
        Assert.Equal(output["category"]!.GetValue<string>() != "OutOfScope", output["isCodeRelated"]!.GetValue<bool>());
        Assert.NotNull(output["reasoning"]!.GetValue<string>());
        Assert.True(output["confidenceScore"]!.GetValue<double>() is > 0 and <= 1);
    }

    // ── Demo resolution severity (ported from the severityAssessment template) ──────────

    [Theory]
    [InlineData("SQL injection", "found", "Critical")]
    [InlineData("Security issue", "found", "Critical")]
    [InlineData("NullReferenceException", "found", "High")]
    [InlineData("App", "crashes on start", "High")]
    [InlineData("Blank total", "nothing renders", "Medium")]
    public void DemoSeverity_FollowsKeywords(string subject, string body, string expected)
    {
        var output = Resolve(subject, body, "X.cs");

        Assert.Equal(expected, output["severityAssessment"]!.GetValue<string>());
    }

    // ── Agent assignment (category → role; ported from the map step lookups) ────────────

    [Theory]
    [InlineData("BackendBug", "BackendDeveloper")]
    [InlineData("FrontendBug", "FrontendDeveloper")]
    [InlineData("QualityTestIssue", "QAEngineer")]
    public void AgentMapping_EveryTeamAndCategoryResolvesToADeclaredAgent(string category, string role)
    {
        Assert.Equal(role, Roles[category]);
        foreach (var team in new[] { "TeamA", "TeamB" })
        {
            var agent = SupportPlatform.Definition.FindAgent($"{team}_{role}");
            Assert.NotNull(agent);
            Assert.Equal(team, agent!.Group);
            Assert.Equal(role, agent.Role);
        }
    }

    [Fact]
    public void AgentMapping_OutOfScopeHasNoAgentRole() =>
        Assert.DoesNotContain("OutOfScope", Categories);

    // ── Demo pull request (paths under dummyAppsRoot; ported from generate_code demo) ───

    [Theory]
    [InlineData("application-a", "OrderController")]
    [InlineData("application-b", "UserController")]
    public void DemoPullRequest_PathStartsWithDummyAppsRoot(string folder, string component)
    {
        var located = ToolTests.Locate(folder, $"Problem in {component}");
        var output = GenerateCode(located.Component, "fix it", located.Path);

        var root = SupportPlatform.Definition.Config["dummyAppsRoot"]!.GetValue<string>();
        var path = output["affectedFilePaths"]!.AsArray()[0]!.GetValue<string>();

        Assert.StartsWith($"{root}/{folder}/", path);
        Assert.True(File.Exists(Path.Combine(SupportPlatform.ModuleFolder, path)));
        Assert.Equal($"[Demo] Fix {located.Component}", output["title"]!.GetValue<string>());
    }

    private static JsonObject Classify(string subject, string body) =>
        (JsonObject)Demo.Resolve("support-email", "classify", $"Subject: {subject}\n\nBody: {body}", Run());

    private static JsonObject Resolve(string subject, string body, string component) =>
        (JsonObject)Demo.Resolve("support-email", "resolve", $"Subject: {subject}\n\nBody: {body}\n\nComponent: {component}", Run());

    private static JsonObject GenerateCode(string component, string fix, string path) =>
        (JsonObject)Demo.Resolve("support-email", "generate_code", $"Component: {component}\n\nProposedFix: {fix}\n\nPath: {path}", Run());

    private static RunContext Run()
    {
        var descriptor = SupportPlatform.Definition.FindWorkflow("support-email")!;
        return new RunContext(Guid.NewGuid(), SupportPlatform.Definition, descriptor, [], null, CancellationToken.None);
    }
}
