namespace Flowgent.Modules.Support.Tests;
// TODO(FEAT-004): port to migrated declarative support YAML + descriptor model; original assertions preserved below, do not delete.
// Temporarily removed from compilation during the MAF declarative migration (FEAT-002): this suite is built on the
// deleted custom step model (FindStep/RuleStep/MapStep/LlmStep.Demo/RuleMatcher over RuleDefinition). FEAT-004 must
// remove this #if false and port the suite against the migrated declarative support workflow + WorkflowDescriptor.
#if false

using System.Text.Json.Nodes;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Flowgent.Definitions.Expressions;
using Flowgent.Definitions.Model;
using Flowgent.Definitions.Schemas;
using Flowgent.Runtime.Steps;

/// <summary>
/// Unit tests of the YAML rules of the support workflow (routing, agent mapping, demo classification and severity),
/// evaluated with the engine's own matcher and template engine. Ported from TeamRouterTests, AgentSelectorTests,
/// DemoChatClientTests and the routing/classification property tests.
/// </summary>
public class SupportDefinitionTests
{
    private static readonly WorkflowDefinition Workflow = SupportPlatform.Definition.FindWorkflow("support-email")!;
    private static readonly TemplateEngine Templates = TemplateEngine.Shared;

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

    // ── Team routing (rule step) ───────────────────────────────────────

    [Theory]
    [InlineData("Bug report", "There is a bug in ApplicationA", "TeamA")]
    [InlineData("Application B is broken", "Details here", "TeamB")]
    [InlineData("crash in application a", "lowercase works", "TeamA")]
    [InlineData("Problem", "APPLICATION B returns 500", "TeamB")]
    public void Routing_SingleApplication_ReturnsItsTeam(string subject, string body, string team)
    {
        var (output, failure) = Route(subject, body);

        Assert.Null(failure);
        Assert.Equal(team, output!["teamName"]!.GetValue<string>());
        Assert.Equal(team == "TeamA" ? "application-a" : "application-b", output["applicationFolder"]!.GetValue<string>());
    }

    [Fact]
    public void Routing_BothApplications_IsAmbiguous()
    {
        var (_, failure) = Route("ApplicationA issue", "Also affects Application B");

        Assert.Equal("Ambiguous routing: both ApplicationA and ApplicationB are mentioned.", failure);
    }

    [Fact]
    public void Routing_NoApplication_Fails()
    {
        var (_, failure) = Route("Generic bug", "Something is broken");

        Assert.Equal("Cannot determine affected application: neither ApplicationA nor ApplicationB is mentioned.", failure);
    }

    [Property(MaxTest = 100)]
    public Property Routing_ApplicationA_RoutesToTeamA(NonEmptyString extra)
    {
        var text = Clean(extra.Get);
        var (output, failure) = Route("Bug in ApplicationA", $"There is a problem in Application A system. {text}");
        return (failure is null && output!["teamName"]!.GetValue<string>() == "TeamA").ToProperty();
    }

    [Property(MaxTest = 100)]
    public Property Routing_ApplicationB_RoutesToTeamB(NonEmptyString extra)
    {
        var text = Clean(extra.Get);
        var (output, failure) = Route("Bug in ApplicationB", $"There is a problem in Application B system. {text}");
        return (failure is null && output!["teamName"]!.GetValue<string>() == "TeamB").ToProperty();
    }

    // ── Agent assignment (map step) ────────────────────────────────────

    [Theory]
    [InlineData("BackendBug", "BackendDeveloper")]
    [InlineData("FrontendBug", "FrontendDeveloper")]
    [InlineData("QualityTestIssue", "QAEngineer")]
    public void AgentMapping_MapsCategoryToRole(string category, string role)
    {
        var lookup = ((MapStep)Workflow.FindStep("assign_agent")!).Lookups!["roleByCategory"];

        Assert.Equal(role, lookup[category]!.GetValue<string>());
    }

    [Property(MaxTest = 100)]
    public Property AgentMapping_EveryTeamAndCategoryResolvesToADeclaredAgent(bool teamA, PositiveInt categoryIndex)
    {
        var team = teamA ? "TeamA" : "TeamB";
        var category = Categories[categoryIndex.Get % Categories.Length];
        var agentId = $"{team}_{Roles[category]}";
        var agent = SupportPlatform.Definition.FindAgent(agentId);
        return (agent is not null && agent.Group == team && agent.Role == Roles[category]).ToProperty();
    }

    [Fact]
    public void AgentMapping_OutOfScopeHasNoRole()
    {
        var lookup = ((MapStep)Workflow.FindStep("assign_agent")!).Lookups!["roleByCategory"];

        Assert.False(lookup.ContainsKey("OutOfScope"));
    }

    // ── Demo classification (llm demo rules) ───────────────────────────

    [Theory]
    [InlineData("NullReferenceException in OrderController - Application A", "The API crashes", "BackendBug")]
    [InlineData("Blank total price in OrderSummary component", "The .razor component renders blank", "FrontendBug")]
    [InlineData("Flaky test due to hardcoded date", "The test fails every other day", "QualityTestIssue")]
    [InlineData("How do I reset my password?", "I forgot my password.", "OutOfScope")]
    public void DemoClassification_FollowsTheKeywordRules(string subject, string body, string expected)
    {
        var output = DemoClassification(subject, body);

        Assert.Equal(expected, output["category"]!.GetValue<string>());
        Assert.Equal(expected != "OutOfScope", output["isCodeRelated"]!.GetValue<bool>());
    }

    // Every demo classification is a valid structured output and consistent (OutOfScope ⇔ not code related).
    [Property(MaxTest = 200)]
    public Property DemoClassification_IsConsistentAndMatchesTheSchema(NonNull<string> subject, NonNull<string> body)
    {
        var output = DemoClassification(subject.Get, body.Get);
        var schema = SupportPlatform.Definition.GetSchema(((LlmStep)Workflow.FindStep("classify")!).Output.Schema);
        var consistent = output["isCodeRelated"]!.GetValue<bool>() == (output["category"]!.GetValue<string>() != "OutOfScope");
        return (consistent && JsonSchemaValidator.Validate(schema, output).Count == 0).ToProperty();
    }

    // ── Demo resolution severity (template) ────────────────────────────

    [Theory]
    [InlineData("SQL injection", "found", "Critical")]
    [InlineData("Security issue", "found", "Critical")]
    [InlineData("NullReferenceException", "found", "High")]
    [InlineData("App", "crashes on start", "High")]
    [InlineData("Blank total", "nothing renders", "Medium")]
    public void DemoSeverity_FollowsKeywords(string subject, string body, string expected)
    {
        var demo = ((LlmStep)Workflow.FindStep("resolve")!).Demo!.Output!.AsObject();
        var scope = Scope(subject, body, new JsonObject { ["locate_component"] = new JsonObject { ["component"] = "X.cs" } });

        Assert.Equal(expected, Templates.Render(demo["severityAssessment"]!.GetValue<string>(), scope));
    }

    // Demo code changes only touch files under config.dummyAppsRoot (they come from the component lookup).
    [Property(MaxTest = 100)]
    public Property DemoPullRequest_PathsStartWithDummyAppsRoot(bool appA, PositiveInt fileIndex)
    {
        var folder = appA ? "application-a" : "application-b";
        var files = Directory.GetFiles(Path.Combine(SupportPlatform.ModuleFolder, "dummy-apps", folder), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .ToList();
        var file = files[fileIndex.Get % files.Count];
        var located = ToolTests.Locate(folder, $"Problem in {Path.GetFileName(file)}");

        var demo = ((LlmStep)Workflow.FindStep("generate_code")!).Demo!.Output!;
        var scope = Scope("s", "b", new JsonObject
        {
            ["locate_component"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(located, Flowgent.Module.Sdk.ModuleTool<object, object>.JsonOptions)),
            ["resolve"] = new JsonObject { ["affectedComponent"] = located.Component, ["proposedFixSummary"] = "fix" },
        });
        var pullRequest = Templates.EvaluateValue(demo, scope)!;
        var root = SupportPlatform.Definition.Config["dummyAppsRoot"]!.GetValue<string>();

        return pullRequest["affectedFilePaths"]!.AsArray()
            .All(p => p!.GetValue<string>().StartsWith($"{root}/{folder}/", StringComparison.Ordinal)
                && File.Exists(Path.Combine(SupportPlatform.ModuleFolder, p.GetValue<string>())))
            .ToProperty();
    }

    private static (JsonNode? Output, string? Failure) Route(string subject, string body)
    {
        var step = (RuleStep)Workflow.FindStep("route_team")!;
        var scope = Scope(subject, body);
        var value = Templates.Render(step.Value, scope);
        var matches = RuleMatcher.Match(step.Rules, value, step.IgnoreCase, scope, Templates);
        return matches.Count switch
        {
            0 => (null, Templates.Render(step.OnNoMatch!.Fail!, scope)),
            > 1 => (null, Templates.Render(step.OnMultipleMatches!.Fail!, scope)),
            _ => (Templates.EvaluateValue(step.Rules[matches[0]].Output, scope), null),
        };
    }

    private static JsonObject DemoClassification(string subject, string body)
    {
        var demo = ((LlmStep)Workflow.FindStep("classify")!).Demo!;
        var scope = Scope(subject, body);
        var matches = RuleMatcher.Match(demo.Rules!, Templates.Render(demo.Value!, scope), ignoreCase: true, scope, Templates);
        return Templates.EvaluateValue(matches.Count > 0 ? demo.Rules![matches[0]].Output : demo.Default, scope)!.AsObject();
    }

    private static EvaluationScope Scope(string subject, string body, JsonObject? steps = null) => new(new JsonObject
    {
        ["input"] = new JsonObject { ["subject"] = subject, ["body"] = body },
        ["steps"] = steps ?? [],
        ["config"] = SupportPlatform.Definition.Config.DeepClone(),
        ["run"] = new JsonObject { ["id"] = Guid.NewGuid().ToString() },
    });

    // Random text must not accidentally mention the other application.
    private static string Clean(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, "(?i)application", "app");
}

#endif
