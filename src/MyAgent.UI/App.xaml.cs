using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyAgent.Core.Data;
using MyAgent.Core.Extensions;
using MyAgent.Skills.Browser;
using MyAgent.Skills.Extensions;
using MyAgent.UI.Services;
using MyAgent.UI.ViewModels;

namespace MyAgent.UI;

public partial class App : Application
{
    private readonly IHost _host;
    public IServiceProvider Services => _host.Services;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls("http://*:5000"); // 监听任意 IP 5000 端口
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        // 建立一个名为 /api/agent/trigger 的接收断点
                        endpoints.MapPost("/api/agent/trigger", async context =>
                        {
                            try
                            {
                                using var reader = new StreamReader(context.Request.Body);
                                var body = await reader.ReadToEndAsync();
                                
                                // 解析收到的通用载荷
                                // 如 { "skill_id": "system.diagnose", "prompt": "网页报错了怎么弄" }
                                var requestJson = JsonDocument.Parse(body).RootElement;
                                string targetSkillId = requestJson.GetProperty("skill_id").GetString() ?? "";
                                string userPrompt = requestJson.GetProperty("prompt").GetString() ?? "";

                                // 利用 ServiceLocator 获取主界面 ViewModel，触发 UI 层调度
                                var mainVm = _host.Services.GetRequiredService<MainViewModel>();
                                
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    mainVm.ParamPrompt = userPrompt;
                                    mainVm.LogMessage($"[Webhook 远端呼叫] 接收到远程操控，触发技能：{targetSkillId}");
                                    
                                    // 找到并选中技能
                                    foreach (var s in mainVm.Skills)
                                    {
                                        if (s.SkillId == targetSkillId)
                                        {
                                            mainVm.SelectedSkill = s;
                                            mainVm.ExecuteSkillCommand.Execute(null);
                                            break;
                                        }
                                    }
                                });

                                await context.Response.WriteAsJsonAsync(new { code = 200, msg = "Agent Triggered Successfully." });
                            }
                            catch (Exception ex)
                            {
                                await context.Response.WriteAsJsonAsync(new { code = 500, error = ex.Message });
                            }
                        });
                    });
                });
            })
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddDebug();
                });

                services.AddMyAgentCore();
                services.AddMyAgentSkills();

                // 替换注入为真实的带界面的浏览器
                services.Remove(ServiceDescriptor.Singleton<IBrowserRenderer, BrowserRendererCore>());
                
                // MainWindow 需要作为单例以便在被渲染器注入时是同一个对象
                services.AddSingleton<MainWindow>();
                services.AddSingleton<IBrowserRenderer, BrowserRendererWpf>();

                services.AddSingleton<MainViewModel>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        await _host.StartAsync();

        // 像之前 Task 1.3 那样自动建库建表
        var dbInit = _host.Services.GetRequiredService<DatabaseInitializer>();
        dbInit.Initialize();

        // 将 Skills 库里面所有的 [SkillAction] 注册进入引擎的反射路由中
        var actionFactory = _host.Services.GetRequiredService<MyAgent.Core.Factories.IActionFactory>();
        actionFactory.RegisterActionsFromAssembly(typeof(MyAgent.Skills.Browser.BrowserRendererCore).Assembly);

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
