# Dokploy Aspire Integration

This repository contains an Aspire AppHost integration for deploying compute resources to Dokploy.

## Where the integration lives

The integration lives in the package project at `/src/Ridder.Hosting.Dokploy`.

The sample AppHost in `/samples/DokployDeploy.AppHost` consumes that package project and shows the intended public API.

## Current capabilities

- Creates or reuses a Dokploy project for an Aspire environment.
- Supports two registry strategies:
  - **Self-hosted on Dokploy**: Dokploy hosts the registry and deployment prompts for the registry domain.
  - **Hosted registry**: deployment prompts for an existing registry URL, username, and password.
- Prompts for the Dokploy API URL and API key during deploy instead of hardcoding connection details.
- Builds and pushes app images, configures Dokploy Docker provider settings, and triggers application deploys.
- Creates Dokploy-managed domains for external Aspire endpoints while preserving the endpoint scheme and port.
- Reconciles application mounts idempotently so repeated deploys do not create duplicate volumes.

## Primary API

The integration is now environment-first.

1. Create a Dokploy environment.
2. Choose a registry strategy for that environment.
3. Opt individual compute resources into Dokploy publishing.

```csharp
var dokploy = builder.AddDokployEnvironment("dokploydeploy")
  .WithHostedRegistry();

builder.AddProject<Projects.MyApi>("api")
  .PublishToDokploy(dokploy);
```

## Registry configuration

Prompted self-hosted registry:

```csharp
var dokploy = builder.AddDokployEnvironment("dokploydeploy")
  .WithSelfHostedRegistry();
```

Prompted hosted registry:

```csharp
var dokploy = builder.AddDokployEnvironment("dokploydeploy")
  .WithHostedRegistry();
```

Explicit values are also supported:

```csharp
builder.AddDokployEnvironment("dokploydeploy")
  .WithSelfHostedRegistry("registry.example.com");

builder.AddDokployEnvironment("dokploydeploy")
  .WithHostedRegistry("registry.example.com", "username", "password");
```

## Publishing applications

Only resources that call `PublishToDokploy(...)` are provisioned in Dokploy.

```csharp
var dokploy = builder.AddDokployEnvironment("dokploydeploy")
  .WithHostedRegistry();

builder.AddProject<Projects.MyApi>("api")
  .PublishToDokploy(dokploy, options =>
  {
    options.ApplicationName = "custom-api";
    options.ConfigureEnvironmentVariables = true;
    options.ConfigureMounts = true;
    options.CreateDomainsForExternalEndpoints = true;
  });
```

## Compatibility APIs

The older convenience methods are still available and forward to the new environment-first API:

```csharp
builder.AddDokployProject(name);
builder.AddDokployProjectSelfHostedRegistry(name);
builder.AddDokployProjectHostedRegistry(name);
```

`AddDokployProject(name)` maps to `AddDokployEnvironment(name).WithSelfHostedRegistry()`.

## Sample

The sample AppHost in `/samples/DokployDeploy.AppHost/AppHost.cs` uses the current recommended shape:

```csharp
var dokploy = builder.AddDokployEnvironment("dokploydeploy")
  .WithHostedRegistry();

var cache = builder.AddRedis("cache")
  .WithDataVolume("cache-data")
  .PublishToDokploy(dokploy);
```

## Internal structure

- `/src/Ridder.Hosting.Dokploy/DokployExtensions.cs` contains the public builder extension surface.
- `/src/Ridder.Hosting.Dokploy/DokployProjectEnvironmentResource.cs` owns the Aspire resource and pipeline wiring.
- `/src/Ridder.Hosting.Dokploy/DokployEnvironmentProvisioner.cs` coordinates registry preparation and application provisioning.
- `/src/Ridder.Hosting.Dokploy/DokployApi*.cs` contains Dokploy API orchestration split by responsibility.
- `/src/Ridder.Hosting.Dokploy/DokployJsonPayload.cs` and `/src/Ridder.Hosting.Dokploy/DokployResponseReaders.cs` contain parsing helpers that can be unit tested independently.

## Known limitations

- Resource-to-resource environment wiring is still incomplete.
- The integration is not yet NuGet-ready; it still needs broader behavioral coverage, more extraction around application and environment mapping, and release packaging hardening.
