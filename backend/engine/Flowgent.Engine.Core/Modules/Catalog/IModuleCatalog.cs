namespace Flowgent.Engine.Core.Modules.Catalog;

using Flowgent.Contracts.Modules;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Engine.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Modules known to the engine and their loaded definitions.</summary>
public interface IModuleCatalog
{
    IReadOnlyList<ModuleState> Modules { get; }

    ModuleState? FindModule(string moduleId);

    MafWorkflowHandle? FindWorkflow(string workflowId);
}
