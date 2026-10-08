using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MrtRouteSimulator.Automation;
using MrtRouteSimulator.Mcp;

var rootIndex = Array.IndexOf(args, "--workspace-root");
if (rootIndex < 0 || rootIndex + 1 >= args.Length)
{
    Console.Error.WriteLine("Usage: MrtRouteSimulator.Mcp --workspace-root <absolute workspace directory>");
    return 2;
}
var builder = Host.CreateApplicationBuilder();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(new WorkspaceFiles(args[rootIndex + 1]));
builder.Services.AddSingleton<SimulationAutomationSession>();
builder.Services.AddSingleton<MrtTools>();
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<MrtTools>();
await builder.Build().RunAsync();
return 0;
