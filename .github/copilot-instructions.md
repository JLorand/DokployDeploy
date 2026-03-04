# Copilot Instructions for DokployDeploy

## Project objective

- The primary objective of this repository is to build and harden the **Dokploy deployment integration for Aspire**, centered in:
  - `DokployDeploy.AppHost\AppHost.cs`
  - `DokployDeploy.AppHost\dokploy.cs`
- Deployment behavior in this repo is specifically driven by **`aspire deploy`** (deploy pipeline integration), not just local run orchestration.
- Prioritize changes that improve Dokploy deployment orchestration, provider API behavior, configuration/security handling, and integration-test coverage.
- Treat `DokployDeploy.ApiService` and `DokployDeploy.Web` mainly as workload/sample apps used to validate deployment integration behavior.

## Build, test, and lint commands

```powershell
# Restore/build the solution
dotnet restore .\DokployDeploy.slnx
dotnet build .\DokployDeploy.slnx

# Run the Aspire application (foreground)
aspire run

# Run the Aspire application in background/isolation (recommended for agent workflows)
aspire run --detach --isolated

# Run deployment pipeline (primary path for Dokploy integration validation)
aspire deploy

# Run all tests
dotnet test .\DokployDeploy.Tests\DokployDeploy.Tests.csproj

# Run a single test
dotnet test .\DokployDeploy.Tests\DokployDeploy.Tests.csproj --filter "FullyQualifiedName~DokployDeploy.Tests.WebTests.GetWebResourceRootReturnsOkStatusCode"
```

No repository-specific lint command is currently configured.

## Website inspection workflow (Playwright)

- For any website/UI inspection (navigation, interaction checks, visual checks, console/network checks), use **playwright-cli** by default.
- Use it to validate `webfrontend` behavior and service integration results instead of relying on code-only assumptions.
- Typical flow:
  1. Start the app (`aspire run` or `aspire run --detach --isolated`).
  2. Open/navigate with `playwright-cli` to the web endpoint.
  3. Use snapshots plus console/network inspection when investigating issues.

## High-level architecture

- `DokployDeploy.AppHost` is the orchestration root for the distributed app and is the source of truth for resource topology:
  - Redis resource: `cache`
  - API service resource: `apiservice`
  - Web resource: `webfrontend`
- `DokployDeploy.AppHost\dokploy.cs` contains the custom Aspire deployment integration for Dokploy:
  - `AddDokployProject(...)` extension method
  - custom resource implementing `IContainerRegistry`
  - deploy pipeline steps that prepare registry and provision/deploy applications through Dokploy APIs
- `DokployDeploy.ServiceDefaults` provides shared cross-cutting defaults used by app services:
  - service discovery
  - default resilience handlers
  - OpenTelemetry setup
  - default health endpoints via `MapDefaultEndpoints()`
- `DokployDeploy.ApiService` and `DokployDeploy.Web` are workload projects wired by AppHost; `DokployDeploy.Web` calls `apiservice` via service discovery (`https+http://apiservice`) and uses Redis output cache (`cache`).
- `DokployDeploy.Tests` uses `Aspire.Hosting.Testing` to boot the full AppHost model and validate `webfrontend` health and root endpoint behavior.

## Key repository conventions

- Keep AppHost resource names stable (`cache`, `apiservice`, `webfrontend`): these names are coupled across AppHost wiring, service discovery in `Web`, and integration tests.
- Every service project should continue using the shared defaults pattern (`builder.AddServiceDefaults()` and `app.MapDefaultEndpoints()`), because AppHost health checks and test expectations depend on it.
- Treat `DokployDeploy.AppHost\dokploy.cs` as the single integration point for deployment-provider orchestration; changes to deployment behavior should be centralized there.
- The Dokploy integration intentionally uses preview Aspire APIs with `#pragma` suppressions (e.g., `ASPIREPIPELINES001`, `ASPIRECOMPUTE003`); keep suppressions scoped to the affected code paths.
- AppHost changes generally require restarting Aspire (`aspire run` / `aspire run --detach --isolated`) to apply model updates.
- For deployment-integration work, prefer validating changes through `aspire deploy` to exercise the actual pipeline hooks.
- For operational debugging, prefer Aspire resource telemetry/log inspection workflow (resource state, structured logs, traces) before changing orchestration code.
- When validating browser-facing behavior, prefer playwright-cli inspection as part of the verification loop.
