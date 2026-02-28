var builder = DistributedApplication.CreateBuilder(args);

// var dockerenv = builder.AddDockerComposeEnvironment("dockerenv");

builder.AddDokployProject("dokploydeploy");

var cache = builder.AddRedis("cache");

var apiService = builder.AddProject<Projects.DokployDeploy_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.DokployDeploy_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WaitFor(cache)
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();




