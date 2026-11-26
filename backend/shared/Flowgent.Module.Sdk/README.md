# Flowgent.Module.Sdk

The small library every module service uses. It publishes the module definition folder to the engine, hosts the module tools, and lets the module submit inputs to the engine.

## What is inside

| File | Role |
|------|------|
| `ModuleSdkExtensions.cs` | **Core.** `AddFlowgentModule` and `MapFlowgentModule`: one call each to set up a module service. |
| `ModuleDefinitionProvider.cs` | **Core.** Reads the definition folder at startup and serves it as the module package (`GET /_module/package`). |
| `ModuleTool.cs`, `IModuleTool.cs`, `ModuleToolRegistry.cs`, `ToolInvocation.cs`, `ToolException.cs` | **Core.** Typed tools served at `POST /_module/tools/{name}`. |
| `IEngineClient.cs`, `EngineClient.cs`, `EngineSubmitResult.cs` | **Core.** Submit an input to the engine. |
| `ModuleDefinitionAssert.cs` | **Support.** Test helper that checks a definition is valid. |
| `ModuleStartupValidator.cs`, `ModuleSdkOptions.cs` | **Support.** Startup check and the `Flowgent:Module` options. |

## Rules

- Depends only on `Flowgent.Contracts` and `Flowgent.Definitions`.
- Must not reference the engine projects. Module services reference this SDK, `Flowgent.Contracts`, `Flowgent.Definitions` and `Flowgent.ServiceDefaults`, and nothing else from the platform.
