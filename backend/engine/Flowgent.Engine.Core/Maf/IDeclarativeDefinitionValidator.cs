namespace Flowgent.Engine.Core.Maf;

using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Flowgent.Contracts.Api;
using Flowgent.Definitions.Model;

/// <summary>
/// Trial-builds each workflow descriptor's declarative YAML so a malformed document is reported at load time.\r\n/// Lives in the seam (it needs the
/// declarative builder); <see cref="Flowgent.Engine.Core.Modules.ModuleCatalog"/> injects it so the Definitions layer stays
/// declarative-free.
/// </summary>
public interface IDeclarativeDefinitionValidator
{
    /// <summary>Trial-builds each descriptor; returns one <see cref="DefinitionErrorInfo"/> per workflow that fails to build.</summary>
    IReadOnlyList<DefinitionErrorInfo> Validate(string moduleId, IReadOnlyList<WorkflowDescriptor> workflows);
}
