using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyAgent.Core.Attributes;
using MyAgent.Core.Interfaces;
using MyAgent.Core.Models;
using MyAgent.Skills.Browser;
using Newtonsoft.Json.Linq;

namespace MyAgent.Skills.Browser;

[SkillAction("browser.navigate")]
public class BrowserNavigateTool : IActionTool
{
    private readonly IBrowserRenderer _browser;
    private readonly ILogger<BrowserNavigateTool> _logger;

    public string ActionType => "browser.navigate";

    public BrowserNavigateTool(IBrowserRenderer browser, ILogger<BrowserNavigateTool> logger)
    {
        _browser = browser;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(SkillExecutionContext context, JToken parameters, CancellationToken cancellationToken)
    {
        string url = parameters["url"]?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(url))
            return ActionResult.Fail("Missing parameter: url");

        try
        {
            _logger.LogInformation("Navigating to: {Url}", url);
            
            // WebView2 navigation is asynchronous
            var navTask = _browser.NavigateAsync(url);
            
            // Wait for navigation to complete or cancel if token triggers
            var tcs = new TaskCompletionSource<bool>();
            using var reg = cancellationToken.Register(() => tcs.TrySetCanceled());
            
            var completedTask = await Task.WhenAny(navTask, tcs.Task);
            await completedTask; // 这将解包并抛出 navTask 中实际包含的任何异常 (如果完成的是 navTask)
            
            cancellationToken.ThrowIfCancellationRequested();

            return ActionResult.Success();
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Navigation to {Url} timed out or was canceled.", url);
            return ActionResult.Fail("Browser navigation failed: 探测超时！请求已被中断。如果开启了全局代理，请您检查填写的 Socks5/Http 代理软件是否正常启动且端口匹配（常见：1080/7890/10808 等），或可尝试加长该节点的 timeout_ms。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to {Url}", url);
            return ActionResult.Fail($"Browser navigation failed: {ex.Message}");
        }
    }
}
