var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres").WithPgAdmin();
var faktuboh = postgres.AddDatabase("faktuboh");

builder.AddProject<Projects.Faktuboh_Api>("api")
    .WithReference(faktuboh)
    .WaitFor(faktuboh);

builder.Build().Run();
