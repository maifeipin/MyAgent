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

[SkillAction("os.mouse")]
public class OSMouseTool : IActionTool
{
    private readonly ILogger<OSMouseTool> _logger;
    public string ActionType => "os.mouse";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    public OSMouseTool(ILogger<OSMouseTool> logger)
    {
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(SkillExecutionContext context, JToken parameters, CancellationToken cancellationToken)
    {
        // 期望参数： {"action": "click", "x": 100, "y": 200, "smooth": true}
        var action = parameters["action"]?.ToString() ?? "click";
        var xToken = parameters["x"];
        var yToken = parameters["y"];

        if (xToken == null || yToken == null)
            return ActionResult.Fail("Missing target coordinates x, y");

        int targetX = xToken.Value<int>();
        int targetY = yToken.Value<int>();
        bool smooth = parameters["smooth"]?.Value<bool>() ?? true;

        try
        {
            if (smooth)
            {
                // TODO: 在更复杂的实现中，此处理应替换为贝塞尔曲线和非匀速抖动算法。
                // 暂时实现简单的线性插值平移
                await SimulateSmoothMouseMoveAsync(targetX, targetY, cancellationToken);
            }
            else
            {
                SetCursorPos(targetX, targetY);
            }

            if (action == "click")
            {
                // 模拟物理按压延迟 (10-50ms)
                mouse_event(MOUSEEVENTF_LEFTDOWN, targetX, targetY, 0, 0);
                await Task.Delay(new Random().Next(10, 50), cancellationToken);
                mouse_event(MOUSEEVENTF_LEFTUP, targetX, targetY, 0, 0);
                
                _logger.LogInformation("Physical mouse click at ({X}, {Y})", targetX, targetY);
            }

            return ActionResult.Success();
        }
        catch (Exception ex)
        {
            return ActionResult.Fail($"OS Mouse action failed: {ex.Message}");
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    private async Task SimulateSmoothMouseMoveAsync(int targetX, int targetY, CancellationToken cancellationToken)
    {
        if (!GetCursorPos(out POINT current)) return;

        int steps = 20; // 假装20帧
        for (int i = 1; i <= steps; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            int dx = targetX - current.X;
            int dy = targetY - current.Y;

            int stepX = current.X + (dx * i / steps);
            int stepY = current.Y + (dy * i / steps);

            SetCursorPos(stepX, stepY);
            
            // 每次移动增加伪随机干扰
            await Task.Delay(new Random().Next(2, 5), cancellationToken);
        }
    }
}
