# Dokploy Aspire Integration

This repository contains an Aspire AppHost integration for deploying resources to Dokploy.

## Where the integration lives

The integration is currently implemented directly in:

- `DokployDeploy.AppHost\\dokploy.cs`

## Current capabilities

- Creates or reuses a Dokploy project.
- Supports two registry modes:
  - **Self-hosted on Dokploy** (requires a registry domain URL).
  - **Externally hosted registry** (requires registry URL, username, and password).
- Builds and pushes app images, configures Docker provider settings, and triggers app deploys.
- Ensures each created application has a domain (currently using a fixed port value).

## Extension methods

```csharp
builder.AddDokployProjectSelfHostedRegistry(name, registryDomainUrl);
builder.AddDokployProjectHostedRegistry(name, registryUrl, username, password);
```

Backward compatibility is preserved:

```csharp
builder.AddDokployProject(name);
```

`AddDokployProject(name)` maps to the self-hosted mode with a default registry domain.

## Example usage

Self-hosted registry mode:

```csharp
builder.AddDokployProjectSelfHostedRegistry("dokploydeploy", "aspirecli.dev");
```

Hosted registry mode:

```csharp
builder.AddDokployProjectHostedRegistry(
    "dokploydeploy",
    "registry.example.com",
    "registry-user",
    "registry-password");
```

## Known limitations

- Dokploy API base URL is still hardcoded in the current implementation.
- Application domain creation currently uses a fixed port (`8080`).
- Resource-to-resource environment wiring is still incomplete.
- The integration is not yet packaged as a reusable standalone library.
