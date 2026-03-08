using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyAgent.Core.Attributes;
using MyAgent.Core.Interfaces;
using MyAgent.Core.Models;
using Newtonsoft.Json.Linq;

namespace MyAgent.Skills.OS;

[SkillAction("os.keyboard")]
public class OSKeyboardTool : IActionTool
{
    private readonly ILogger<OSKeyboardTool> _logger;
    public string ActionType => "os.keyboard";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
    
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public OSKeyboardTool(ILogger<OSKeyboardTool> logger)
    {
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(SkillExecutionContext context, JToken parameters, CancellationToken cancellationToken)
    {
        string text = parameters["text"]?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(text))
            return ActionResult.Fail("Missing parameter: text");

        try
        {
            var random = new Random();
            foreach (char c in text)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // 为了稳健性和兼容性，使用 SendKeys（内部封装也是 user32 API 的便捷方式）
                // 若对系统底层要求极高，应使用 SendInput PInvoke 数组。在此用更稳定的高层包装展示。
                System.Windows.Forms.SendKeys.SendWait(c.ToString());
                
                // 模拟人类按键频率，每次敲击有 50-150ms 间隔
                await Task.Delay(random.Next(50, 150), cancellationToken);
            }

            _logger.LogInformation("Physical keyboard input finished: '{Text}'", text);
            return ActionResult.Success();
        }
        catch (Exception ex)
        {
            return ActionResult.Fail($"OS Keyboard action failed: {ex.Message}");
        }
    }
}
