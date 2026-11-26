# Flowgent.Definitions

Reads and checks a module definition folder (`module.yaml`, workflows, prompts, schemas, samples) and turns it into plain objects.

## What is inside

| Folder or file | Role |
|----------------|------|
| `ModuleLoader.cs`, `ModuleDefinition.cs` | **Core.** Load a module folder, validate it, and keep the result. |
| `Model/` | **Core.** Records for the manifest: agents, tools, workflows, terminals, schemas. |
| `Yaml/` | **Support.** YAML to JSON reading with line numbers for error messages. |
| `Schemas/` | **Support.** JSON Schema validation and the embedded schemas for `module.yaml` and samples. |
| `IWorkflowGraphProjector.cs` | The interface that draws the workflow graph. The real implementation is in `Flowgent.Engine.Core`; `MinimalWorkflowGraphProjector` is the fallback. |

## Rules

- Depends only on `Flowgent.Contracts`.
- Must not depend on ASP.NET Core or on Microsoft Agent Framework. That is why graph projection and the trial build of workflows are interfaces implemented in `Flowgent.Engine.Core`.
- No domain terms of any module.