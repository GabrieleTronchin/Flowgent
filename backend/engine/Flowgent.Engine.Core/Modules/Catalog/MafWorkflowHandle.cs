namespace Flowgent.Engine.Core.Modules.Catalog;

using Flowgent.Contracts.Modules;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>A workflow descriptor and the module definition (version) it belongs to.</summary>
public sealed record MafWorkflowHandle(ModuleState Module, ModuleDefinition Definition, WorkflowDescriptor Workflow)
{
    public bool IsAvailable => Module.IsAvailable;
}
