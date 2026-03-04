# Dokploy Aspire Integration

This repository contains an Aspire AppHost integration for deploying resources to Dokploy.

## Where the integration lives

The integration is currently implemented directly in:

- `DokployDeploy.AppHost\\dokploy.cs`

## Current capabilities

- Creates or reuses a Dokploy project.
- Supports two registry modes:
  - **Self-hosted on Dokploy** (prompts for registry domain URL during deploy).
  - **Externally hosted registry** (prompts for registry URL, username, and password during deploy).
- For self-hosted registry mode, automatically ensures a compose domain for the registry using:
  - `port: 5000`
  - `https: true`
  - `certificateType: letsencrypt`
  - idempotent host check/update via `domain.byComposeId` + `domain.update`/`domain.create`.
- Builds and pushes app images, configures Docker provider settings, and triggers app deploys.
- Ensures each created application has a domain (currently using a fixed port value).

## Extension methods

```csharp
builder.AddDokployProjectSelfHostedRegistry(name);
builder.AddDokployProjectHostedRegistry(name);
```

Backward compatibility is preserved:

```csharp
builder.AddDokployProject(name);
```

`AddDokployProject(name)` maps to the self-hosted mode and prompts for the registry domain URL.

## Example usage

Self-hosted registry mode (parameter prompt):

```csharp
builder.AddDokployProjectSelfHostedRegistry("dokploydeploy");
```

Hosted registry mode (parameter prompts):

```csharp
builder.AddDokployProjectHostedRegistry("dokploydeploy");
```

Optional explicit-value overloads are still available if you want to hardcode values:

```csharp
builder.AddDokployProjectSelfHostedRegistry(name, registryDomainUrl);
builder.AddDokployProjectHostedRegistry(name, registryUrl, username, password);
```

## Known limitations

- Dokploy API base URL is still hardcoded in the current implementation.
- Application domain creation currently uses a fixed port (`8080`).
- Resource-to-resource environment wiring is still incomplete.
- The integration is not yet packaged as a reusable standalone library.
