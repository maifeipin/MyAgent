using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyAgent.Core.Data;
using MyAgent.Core.Data.Repositories;
using MyAgent.Core.Config;
using MyAgent.Core.Factories;

namespace MyAgent.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMyAgentCore(this IServiceCollection services)
    {
        // 自动解析默认数据库路径
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dbPath = Path.Combine(appData, "MyAgent", "data", "agent.db");
        string connectionString = $"Data Source={dbPath}";

        // Add core framework services here
        services.AddSingleton(sp => new DatabaseInitializer(connectionString, sp.GetRequiredService<ILogger<DatabaseInitializer>>()));
        services.AddSingleton<IExecutionLogRepository>(new ExecutionLogRepository(connectionString));

        // Register Phase 2 Engine Components
        services.AddSingleton<ISkillConfigReader, SkillConfigReader>();
        services.AddSingleton<IActionFactory, ActionFactory>();
        services.AddTransient<MyAgent.Core.Engine.SkillEngine>();
        
        return services;
    }
}
