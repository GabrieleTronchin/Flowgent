namespace Flowgent.Module.Sdk;
/// <summary>Module SDK configuration (section <c>Flowgent:Module</c>).</summary>
public sealed class ModuleSdkOptions
{
    public const string SectionName = "Flowgent:Module";

    /// <summary>Definition folder, absolute or relative to the content root (default: <c>../definition</c>).</summary>
    public string DefinitionPath { get; set; } = "../definition";

    /// <summary>Stop the service when its definition is invalid. Default false, so the engine can show the errors.</summary>
    public bool FailOnInvalidDefinition { get; set; }

    /// <summary>Engine base address for <see cref="IEngineClient"/> (service discovery name under Aspire).</summary>
    public string EngineUrl { get; set; } = "http://localhost:5081";
}
