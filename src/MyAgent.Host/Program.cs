using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyAgent.Core.Data;
using MyAgent.Core.Extensions;
using MyAgent.Skills.Extensions;

namespace MyAgent.Host;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);

        // Configure Logging (ILogger<T>)
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();

        // Register Modules via generic DI Startup
        builder.Services.AddMyAgentCore();
        builder.Services.AddMyAgentSkills();

        var host = builder.Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("MyAgent system host starting...");

        // Run Database Initialization (Task 1.3)
        var dbInit = host.Services.GetRequiredService<DatabaseInitializer>();
        dbInit.Initialize();

        // Keep the host running to listen for schedules and events
        await host.RunAsync();
    }
}
