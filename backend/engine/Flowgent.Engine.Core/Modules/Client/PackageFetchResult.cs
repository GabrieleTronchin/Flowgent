namespace Flowgent.Engine.Core.Modules.Client;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flowgent.Contracts.Modules;
using Microsoft.Extensions.Options;

/// <summary>Result of a package request.</summary>
public abstract record PackageFetchResult
{
    public sealed record Changed(ModulePackage Package) : PackageFetchResult;

    public sealed record NotModified : PackageFetchResult;

    public sealed record Failed(string Error) : PackageFetchResult;
}
