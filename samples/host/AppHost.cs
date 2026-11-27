// Starts the platform with the example modules: engine, gateway (http://localhost:5080), dashboard
// (http://localhost:5173), the support module and its MCP server, and the jokes module.
// Run: dotnet run --project samples/host
using Flowgent.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var platform = FlowgentPlatform.Add<Projects.Flowgent_Engine_Service, Projects.Flowgent_Gateway>(builder, "../../dashboard");

// Support MCP server (workflow serverLabel "support"): the engine reaches the locateComponent tool over Streamable HTTP.
platform.AddMcpServer<Projects.Flowgent_Mcp_Support>("support")
    .WithEnvironment("SUPPORT_DUMMYAPPS_ROOT", Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "support", "dummy-apps")));

// Support: the reference sample (support-email triage with human approval), with its legacy /api/support routes.
platform.AddModule<Projects.Flowgent_Modules_Support>("support", "/api/support/{**catch-all}");

// Jokes: the smallest sample (one agent step).
platform.AddModule<Projects.Flowgent_Modules_Jokes>("jokes");

// The module template (backend/template), off by default here: --AppHost:Modules:template:Enabled=true turns it on.
platform.AddModule<Projects.Flowgent_Modules_Template>("template");

builder.Build().Run();
