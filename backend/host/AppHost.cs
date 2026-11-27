// Starts the core platform: engine, gateway (http://localhost:5080), dashboard (http://localhost:5173) and the module
// template. The example modules have their own AppHost in samples/host.
// Run: dotnet run --project backend/host
using Flowgent.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var platform = FlowgentPlatform.Add<Projects.Flowgent_Engine_Service, Projects.Flowgent_Gateway>(builder, "../../dashboard");

// The module template (backend/template), index 0 like the launch profiles of the engine and gateway.
// --AppHost:Modules:template:Enabled=false leaves it out.
platform.AddModule<Projects.Flowgent_Modules_Template>("template");

builder.Build().Run();
