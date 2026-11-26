namespace Flowgent.Engine.Core.Maf.Building;

using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Flowgent.Contracts.Api;
using Flowgent.Definitions.Model;

/// <summary>
/// Checks that each workflow YAML of a module can really be built, so a broken document is reported when the module is
/// loaded and not when a run starts. It lives with the MAF code because it needs the MAF builder; <see cref="Flowgent.Engine.Core.Modules.Catalog.ModuleCatalog"/>
/// uses it through this interface so the Definitions project does not need MAF.
/// </summary>
public interface IDeclarativeDefinitionValidator
{
    /// <summary>Trial-builds each descriptor; returns one <see cref="DefinitionErrorInfo"/> per workflow that fails to build.</summary>
    IReadOnlyList<DefinitionErrorInfo> Validate(string moduleId, IReadOnlyList<WorkflowDescriptor> workflows);
}
