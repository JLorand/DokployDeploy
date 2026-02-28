# Aspire Deployment Integration Spec (Provider-Agnostic)

## 1) Purpose
Define a repeatable, production-ready pattern for building Aspire deployment integrations, using the Dokploy integration in this repository as the worked example.

## 2) Scope
- In scope:
  - AppHost-side integration architecture
  - Resource modeling and deployment pipeline wiring
  - Provider API orchestration requirements
  - Security, configuration, resiliency, and testing requirements
  - Gap mapping from this repo's Dokploy implementation
- Out of scope:
  - Provider UI/UX design
  - Runtime app business logic (`ApiService`, `Web`) beyond deployment concerns

## 3) Reference Implementation in This Repository
- Integration entrypoint: `DokployDeploy.AppHost\AppHost.cs`
  - `builder.AddDokployProject("dokploydeploy")`
- Integration implementation: `DokployDeploy.AppHost\dokploy.cs`
  - `DokployExtensions.AddDokployProject(...)`
  - `DokployProjectEnvironmentResource : Resource, IContainerRegistry`
  - `DokployApi` (provider HTTP orchestration)

## 4) Core Integration Model

### 4.1 Required Components
1. **Builder extension**  
   Adds provider integration to the distributed app model and collects required deployment inputs.
2. **Provider resource**  
   Represents deployment target state and exposes container-registry endpoint metadata for downstream image push/publish.
3. **Pipeline steps**  
   Hooks into Aspire deploy lifecycle to:
   - prepare provider-side prerequisites,
   - push images,
   - provision/update deployed applications,
   - trigger deployment.
4. **Provider API client**  
   Encapsulates provider endpoints, payload parsing, retries/timeouts, and idempotent create-or-get behavior.

### 4.2 Lifecycle Contract
1. Register provider resource.
2. Annotate compute resources with target registry/publish metadata.
3. On deploy:
   - resolve credentials/config,
   - ensure project/environment exists,
   - ensure registry exists and is usable,
   - configure each app's image/provider settings,
   - trigger provider deployment per app/resource.

## 5) Normative Requirements (MUST/SHOULD)

### 5.1 Configuration & Secrets
- MUST externalize provider base URL and credentials (no hardcoded URLs/passwords in code).
- MUST accept secrets via secure Aspire parameters or secret stores.
- SHOULD support environment-level overrides (dev/stage/prod).

### 5.2 Security
- MUST avoid logging raw secrets.
- MUST use TLS endpoints unless explicitly configured otherwise for local/dev.
- SHOULD validate host/URL formats at startup and fail fast on invalid config.

### 5.3 Reliability
- MUST use idempotent provider operations (get-or-create patterns).
- MUST handle provider responses with strict validation and explicit errors.
- SHOULD include retry strategy for transient HTTP failures.
- SHOULD include operation timeouts and cancellation support.

### 5.4 Deployment Behavior
- MUST map Aspire compute resources to provider applications deterministically.
- MUST ensure registry linkage before saving app docker provider settings.
- SHOULD verify deploy result/health where provider APIs support it.

### 5.5 Observability
- MUST emit structured logs for each major step (project, registry, app config, deploy trigger).
- SHOULD include correlation identifiers per deploy run.

### 5.6 Testing
- MUST include at least one integration-oriented test path for deployment orchestration behavior.
- SHOULD include provider client parsing tests for known payload variants.
- SHOULD include negative-path tests (invalid credentials, missing environment, malformed payloads).

## 6) Implementation Blueprint (Reusable)
1. Add `Add<Provider>Project(...)` extension in AppHost.
2. Define `<Provider>EnvironmentResource : Resource, IContainerRegistry`.
3. Attach pipeline steps:
   - `prepare-registry-*` before deploy
   - `provision-apps-*` after image push and before/at deploy
4. Implement provider client:
   - create configured `HttpClient`
   - implement `GetOrCreateProject`, `GetOrCreateRegistry`, `GetOrCreateApplication`, `DeployApplication`
   - normalize/parse provider payload envelopes
5. Wire registry target annotations for compute resources.
6. Add configuration section and parameter surface for provider URL, API key, registry credentials.
7. Add tests (unit + integration) for orchestration and payload parsing.

## 7) Dokploy Example Mapping

### 7.1 What the Current Implementation Already Demonstrates
- Custom AppHost extension and resource registration.
- Pipeline-driven deployment orchestration.
- Project/registry/application get-or-create API flows.
- Application domain provisioning and deployment trigger hooks.
- Parsing helpers for multiple provider payload shapes.

### 7.2 Current Gaps (from repo code)
1. **Hardcoded provider/registry settings**
   - Dokploy base URL hardcoded in `dokploy.cs`.
   - Registry username/password constants hardcoded.
2. **Configuration mismatch**
   - `appsettings.Development.json` includes `DokployApi` settings, but runtime flow currently uses hardcoded constructor values.
3. **Security/logging**
   - Masking exists for API key preview, but config/credential strategy is incomplete.
4. **Test coverage**
   - Existing tests validate app startup path; no focused coverage for Dokploy orchestration and parser edge cases.
5. **Operational robustness**
   - TODO markers indicate incomplete flow hardening (compose state checks, cleanup/refinement items).

## 8) Completion Criteria for a Production-Ready Integration
- All provider URL/credential values sourced from config/secure parameters.
- No plaintext secrets in source.
- Deterministic and idempotent deploy path for repeated runs.
- Clear failure messages for each provider operation.
- Test suite includes:
  - provider payload parsing variants,
  - deployment orchestration happy path,
  - key failure paths.
- Documentation includes:
  - configuration schema,
  - required secrets,
  - execution order,
  - troubleshooting matrix.

## 9) Suggested Document Structure for Future Provider Integrations
1. Overview and provider capability assumptions
2. Resource model and pipeline step design
3. API contract mapping
4. Configuration and secrets model
5. Error model and retries
6. Test strategy
7. Operational runbook (diagnostics + common failures)
8. Provider-specific deviations from baseline spec

## 10) Notes for This Repository
- `ApiService` and `Web` are sample workloads; integration behavior is centered in `DokployDeploy.AppHost`.
- This spec treats Dokploy as the example implementation, not the only target provider.
