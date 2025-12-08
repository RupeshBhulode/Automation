using DotNet2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient<TrelloClient>();

builder.Services.AddSingleton<GoogleSheetClient>();

builder.Services.AddSingleton<StateManager>();

builder.Services.AddSingleton<SyncLogic>();

builder.Services.AddHostedService<SyncWorker>();


var host=builder.Build();

await host.RunAsync();