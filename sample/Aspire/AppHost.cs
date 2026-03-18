var builder = DistributedApplication.CreateBuilder(args);

var server = builder.AddProject<Projects.Server>("server")
    //.WithArgs("--urls", "http://server.dev.localhost:4242")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// For now, we can't really launch a console project and have its terminal shown.
// See https://github.com/dotnet/aspire/issues/8440
//builder.AddProject<Client>("client")
//    .WithReference(server)
//    // Flow the resolved Server HTTP endpoint to the client config
//    .WithEnvironment("ai__clients__chat__endpoint", server.GetEndpoint("http"))
//    .WithExternalConsole();

builder.Build().Run();
