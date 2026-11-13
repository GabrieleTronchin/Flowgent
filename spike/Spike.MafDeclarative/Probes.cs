using System.Reflection;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Decl = Microsoft.Agents.AI.Workflows.Declarative;

namespace Spike.MafDeclarative;

/// <summary>
/// FEAT-001 behavioral probes (design §2.2). Reference-only, offline, no API key.
///   PROBE-A : a JSON-OBJECT-string MCP result lands as a single-column `Value` table,
///             read as a STRING with First(...).Value (not re-typed to Double / nested table).
///   PROBE-B : the host can read a named Global.* variable from run state after the run.
///   PROBE-C : ParseJSON exists, supports record field access, and its malformed behavior.
/// Also enumerates the [Experimental] diagnostic IDs on the declarative assemblies so the
/// scoped NoWarn on Flowgent.Runtime.csproj can list the exact IDs.
/// Run with:  dotnet run -- probes
/// </summary>
internal static class Probes
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        Console.WriteLine("=== FEAT-001 probes (offline, reference-only) ===");
        DumpExperimentalIds();

        var a = await RunProbeAcAsync(ct);
        var c = await RunProbeCMalformedAsync(ct);
        var b = await RunProbeBAsync(ct);

        Console.WriteLine();
        Console.WriteLine($"[probe-summary] PROBE-A={a.pass} PROBE-C(good)={a.parsePass} " +
                          $"PROBE-C(malformed)={c} PROBE-B={b}");
        return (a.pass && a.parsePass && b) ? 0 : 1;
    }

    private static string WorkflowPath(string file) => Path.Combine(AppContext.BaseDirectory, file);

    // ---- [Experimental] diagnostic id enumeration -------------------------------------
    private static void DumpExperimentalIds()
    {
        Console.WriteLine();
        Console.WriteLine("--- [Experimental] diagnostic IDs on referenced MAF assemblies ---");
        Assembly[] assemblies =
        [
            typeof(DeclarativeWorkflowBuilder).Assembly,     // Microsoft.Agents.AI.Workflows.Declarative
            typeof(Workflow).Assembly,                       // Microsoft.Agents.AI.Workflows
            typeof(Microsoft.Agents.AI.AIAgent).Assembly,    // Microsoft.Agents.AI
        ];

        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var asm in assemblies)
        {
            var asmLevel = CollectIds(asm.GetCustomAttributesData());
            foreach (var id in asmLevel) ids.Add(id);

            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = [.. ex.Types.Where(t => t is not null)!]; }

            foreach (var t in types)
            {
                foreach (var id in CollectIds(t.GetCustomAttributesData())) ids.Add(id);
                foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    foreach (var id in CollectIds(m.GetCustomAttributesData())) ids.Add(id);
                }
            }

            Console.WriteLine($"  {asm.GetName().Name}: [{string.Join(", ", asmLevel)}]");
        }

        Console.WriteLine($"  ALL distinct experimental IDs: [{string.Join(", ", ids)}]");
    }

    private static IEnumerable<string> CollectIds(IList<CustomAttributeData> attrs)
    {
        foreach (var a in attrs)
        {
            if (a.AttributeType.FullName != "System.Diagnostics.CodeAnalysis.ExperimentalAttribute")
            {
                continue;
            }

            if (a.ConstructorArguments.Count > 0 && a.ConstructorArguments[0].Value is string id && id.Length > 0)
            {
                yield return id;
            }
        }
    }

    // ---- PROBE-A ----------------------------------------------------------------------
    private static async Task<(bool pass, bool parsePass)> RunProbeAcAsync(CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("--- PROBE-A: structured MCP result as JSON-object string, read as a record (First(r).field) ---");

        var file = WorkflowPath("workflow.probeA.yaml");
        var options = new DeclarativeWorkflowOptions(new SpikeAgentProvider(new FakeChatClient(), []))
        {
            McpToolHandler = new ProbeMcpToolHandler(),
        };
        Workflow workflow = DeclarativeWorkflowBuilder.Build<string>(
            file, options, inputTransform: t => new ChatMessage(ChatRole.User, t));

        string? final = null;
        var error = false;
        StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, "find Checkout", cancellationToken: ct);
        await foreach (WorkflowEvent evt in run.WatchStreamAsync(ct))
        {
            switch (evt)
            {
                case Decl.MessageActivityEvent act:
                    final = act.Message;
                    Console.WriteLine($"  [MessageActivityEvent] {final}");
                    break;
                case WorkflowErrorEvent err:
                    error = true;
                    Console.WriteLine($"  [WorkflowErrorEvent] {err.Data}");
                    break;
            }
        }

        // PROBE-A passes if the object JSON string did NOT crash (no Double error) and its
        // fields were readable as record fields off the one-row table (First(r).field).
        var pass = !error && final is not null &&
                   final.Contains("path=src/Checkout.cs", StringComparison.Ordinal) &&
                   final.Contains("found=true", StringComparison.Ordinal) &&
                   final.Contains("component=Checkout", StringComparison.Ordinal);

        Console.WriteLine($"  [PROBE-A] object JSON string parsed to a record, fields readable, no Double crash: {pass}");
        return (pass, pass);
    }

    // ---- PROBE-C (malformed) ----------------------------------------------------------
    private static async Task<string> RunProbeCMalformedAsync(CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("--- PROBE-C malformed: ParseJSON on a non-JSON string (error vs blank) ---");

        var file = WorkflowPath("workflow.probeC_malformed.yaml");
        var options = new DeclarativeWorkflowOptions(new SpikeAgentProvider(new FakeChatClient(), []))
        {
            McpToolHandler = new ProbeMcpToolHandler(),
        };
        Workflow workflow = DeclarativeWorkflowBuilder.Build<string>(
            file, options, inputTransform: t => new ChatMessage(ChatRole.User, t));

        string? final = null;
        string? errorText = null;
        try
        {
            StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, "x", cancellationToken: ct);
            await foreach (WorkflowEvent evt in run.WatchStreamAsync(ct))
            {
                switch (evt)
                {
                    case Decl.MessageActivityEvent act:
                        final = act.Message;
                        Console.WriteLine($"  [MessageActivityEvent] {final}");
                        break;
                    case WorkflowErrorEvent err:
                        errorText = err.Data?.ToString();
                        Console.WriteLine($"  [WorkflowErrorEvent] {errorText}");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            errorText = $"{ex.GetType().Name}: {ex.Message}";
            Console.WriteLine($"  [exception] {errorText}");
        }

        var verdict = errorText is not null
            ? "ERROR"
            : (final is not null && final.Contains("not-json-at-all", StringComparison.Ordinal)
                ? "NON-JSON STRING via .Value (continues)"
                : (final is not null ? "CONTINUES(other)" : "UNKNOWN"));
        Console.WriteLine($"  [PROBE-C malformed] behavior = {verdict}");
        return verdict;
    }

    // ---- PROBE-B: read a Global.* variable from run state -----------------------------
    // Mechanism (design §2.2 option 2 / §11): run WITH checkpointing to a JSON store, then
    // read the persisted Global scope from the checkpoint JSON. This is the host-readable
    // surface the seam uses (MafRunDriver reads Global.<stepId> to call SetOutput).
    private static async Task<bool> RunProbeBAsync(CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("--- PROBE-B: read a named Global.* variable from run state via JSON checkpoint ---");

        var dir = Path.Combine(Path.GetTempPath(), $"feat001-probeB-{Guid.NewGuid():N}");
        var checkpointDir = new DirectoryInfo(dir);
        using var store = new FileSystemJsonCheckpointStore(checkpointDir);
        var checkpointManager = CheckpointManager.CreateJson(store);

        var file = WorkflowPath("workflow.probeA.yaml"); // writes Global.locatePath / Global.locateRawStr
        var options = new DeclarativeWorkflowOptions(new SpikeAgentProvider(new FakeChatClient(), []))
        {
            McpToolHandler = new ProbeMcpToolHandler(),
        };
        Workflow workflow = DeclarativeWorkflowBuilder.Build<string>(
            file, options, inputTransform: t => new ChatMessage(ChatRole.User, t));

        StreamingRun run = await InProcessExecution.RunStreamingAsync(
            workflow, "find Checkout", checkpointManager, cancellationToken: ct);

        var checkpointCount = 0;
        await foreach (WorkflowEvent evt in run.WatchStreamAsync(ct))
        {
            if (evt is SuperStepCompletedEvent step && step.CompletionInfo?.Checkpoint is not null)
            {
                checkpointCount++;
            }
        }

        Console.WriteLine($"  super-step checkpoints observed: {checkpointCount}");

        // The JSON checkpoint files on disk contain the serialized scope state. Read every
        // file and look for the Global variable name/value we wrote.
        var found = false;
        string? recovered = null;
        foreach (var jsonFile in checkpointDir.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (jsonFile.Name.Equals("index.jsonl", StringComparison.OrdinalIgnoreCase))
            {
                continue; // still held open by the live store
            }

            string text;
            try
            {
                using var fs = new FileStream(jsonFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                text = await reader.ReadToEndAsync(ct);
            }
            catch (IOException)
            {
                continue;
            }

            // Look for the Global.* variable we wrote (scope key carries the name "locatePath").
            if (text.Contains("locatePath", StringComparison.Ordinal))
            {
                found = true;
                recovered = jsonFile.Name;
                var idx = text.IndexOf("locatePath", StringComparison.Ordinal);
                var start = Math.Max(0, idx - 20);
                Console.WriteLine($"    Global.locatePath found in {jsonFile.Name}: …{text.Substring(start, Math.Min(220, text.Length - start))}…");
                break;
            }
        }

        Console.WriteLine($"  [PROBE-B] Global.* value recoverable from run state (JSON checkpoint): {found}" +
                          (recovered is not null ? $" (file {recovered})" : string.Empty));
        return found;
    }
}
