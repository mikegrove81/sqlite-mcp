using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqliteMcp.Services;
using SqliteMcp.Tools;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<SqliteConnectionFactory>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "sqlite",
            Version = "1.0.0"
        };
    })
    .WithStdioServerTransport()
    .WithTools<SchemaTools>()
    .WithTools<QueryTools>()
    .WithTools<ExecuteTools>()
    .WithTools<ImportTools>();

// MCP uses stdout for transport — all logging must go to stderr
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

var app = builder.Build();
await app.RunAsync();
