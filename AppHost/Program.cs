using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var sqlserver = builder.AddSqlServer("sqlserver")
    .WithLifetime(ContainerLifetime.Persistent); ;

var redis = builder.AddRedis("redis");

var innerApi = builder.AddProject<Projects.Inner_API>("inner-api")
    .WithReference(sqlserver)
    .WaitFor(sqlserver)
    .WithReference(redis)
    .WaitFor(redis);

var mainApi = builder.AddProject<Projects.Main_API>("main-api")
    .WithReference(innerApi)
    .WaitFor(innerApi);

var webApp = builder.AddNpmApp("webapp", "../Frontend")
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("REACT_APP_MAIN_API_URL", mainApi.GetEndpoint("https"))
    .WithReference(mainApi)
    .WaitFor(mainApi);

builder.Build().Run();
