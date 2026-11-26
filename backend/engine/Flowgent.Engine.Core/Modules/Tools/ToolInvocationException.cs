namespace Flowgent.Engine.Core.Modules.Tools;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flowgent.Contracts.Modules;
using Flowgent.Engine.Core.Configuration;
using Microsoft.Extensions.Options;

/// <summary>A tool call failed; <see cref="Exception.Message"/> is suitable for the run detail.</summary>
public sealed class ToolInvocationException(string message, Exception? inner = null) : Exception(message, inner);
