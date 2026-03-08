using Microsoft.Extensions.DependencyInjection;
using MyAgent.Skills.Browser;
using MyAgent.Skills.Perception;
using MyAgent.Skills.OS;
using MyAgent.Skills.AI;

namespace MyAgent.Skills.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMyAgentSkills(this IServiceCollection services)
    {
        // 1. 注册全局受控的 WebView2 托管容器 (必须为单例，以保证 STA 线程长存)
        services.AddSingleton<IBrowserRenderer, BrowserRendererCore>();

        // 2. 注册各个工具 Action (需为 Transient 瞬态或单例，由 ActionFactory 利用依赖注入按需创建)
        services.AddTransient<PerceptionDomTool>();
        services.AddTransient<OSMouseTool>();
        services.AddTransient<OSKeyboardTool>();
        services.AddTransient<MyAgent.Skills.OS.OSFileWriteTool>();
        services.AddTransient<MyAgent.Skills.OS.OsSshExecuteTool>();
        services.AddTransient<MyAgent.Skills.AI.AiSshAgentTool>(); // Interactive SSH LOOP Tool
        services.AddTransient<MyAgent.Skills.Notify.WebhookNotifyTool>();
        services.AddTransient<MyAgent.Skills.Agent.AgentConfigUpdateTool>(); // UI Config Control Tool
        // 3. 注册 Http Client (为外部大模型 API 调用使用池化管理)
        services.AddHttpClient("AiModelsClient", client => 
        {
            client.Timeout = System.TimeSpan.FromSeconds(150); // 给足挂代理访问外部网关响应的空间
        }).ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler 
        { 
            UseProxy = true, 
            Proxy = System.Net.WebRequest.GetSystemWebProxy() 
        });

        // 注册 Webhook 推送专用的短连接 Client
        services.AddHttpClient("NotifyClient", client => 
        {
            client.Timeout = System.TimeSpan.FromSeconds(15);
        }).ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler 
        { 
            UseProxy = true, 
            Proxy = System.Net.WebRequest.GetSystemWebProxy() 
        });

        services.AddTransient<AiAnalyzeTool>();

        return services;
    }
}
