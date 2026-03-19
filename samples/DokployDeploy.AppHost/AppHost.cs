using Ridder.Hosting.Dokploy;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDokployProjectSelfHostedRegistry("dokploydeploy");

var cache = builder.AddRedis("cache")
    .WithDataVolume("cache-data");

var apiService = builder.AddProject<Projects.DokployDeploy_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.DokployDeploy_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();




