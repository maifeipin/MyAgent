using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyAgent.Core.Attributes;
using MyAgent.Core.Interfaces;
using MyAgent.Core.Models;
using MyAgent.Skills.Browser;
using Newtonsoft.Json.Linq;

namespace MyAgent.Skills.Perception;

[SkillAction("perception.dom")]
public class PerceptionDomTool : IActionTool
{
    private readonly IBrowserRenderer _browser;
    private readonly ILogger<PerceptionDomTool> _logger;

    public string ActionType => "perception.dom";

    public PerceptionDomTool(IBrowserRenderer browser, ILogger<PerceptionDomTool> logger)
    {
        _browser = browser;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(SkillExecutionContext context, JToken parameters, CancellationToken cancellationToken)
    {
        string selector = parameters["selector"]?.ToString() ?? string.Empty;
        string extractType = parameters["extract_type"]?.ToString() ?? "text"; // "text", "exists", "rect"

        if (string.IsNullOrEmpty(selector))
            return ActionResult.Fail("Missing parameter: selector");

        try
        {
            string jsSnippet = string.Empty;
            switch (extractType.ToLower())
            {
                case "exists":
                    jsSnippet = $"document.querySelector('{selector}') !== null;";
                    break;
                case "text":
                    jsSnippet = $"(() => {{ let el = document.querySelector('{selector}'); return el ? el.innerText : null; }})();";
                    break;
                case "rect":
                    jsSnippet = $@"
                        (() => {{
                            let el = document.querySelector('{selector}');
                            if (!el) return null;
                            let rect = el.getBoundingClientRect();
                            return JSON.stringify({{ x: rect.x, y: rect.y, width: rect.width, height: rect.height }});
                        }})();";
                    break;
                default:
                    return ActionResult.Fail($"Unknown extract_type: {extractType}");
            }

            var resultStr = await _browser.ExecuteScriptAsync(jsSnippet);
            
            // WebView2 ExecuteScriptAsync 返回的是 JSON 序列化后的字符串（例如带双引号的 "\"xxx\"" 或 "true"）
            if (resultStr == "null" || string.IsNullOrEmpty(resultStr))
                return ActionResult.Fail($"Element '{selector}' not found or returned null.");

            ActionResult actionResult = ActionResult.Success();
            
            if (extractType == "exists")
            {
                actionResult.OutputData["exists"] = resultStr.Trim('"').ToLower() == "true";
            }
            else if (extractType == "text")
            {
                // 正确反序列化来自 WebView2 传回的包含转义 \n 的 json string
                var unescaped = JToken.Parse(resultStr).ToString();
                
                // 【核心边界控制】：如果网页提取出的文本过长（如数万字），会导致 AI 分析陷入漫长的等待（甚至触发 60s 强制掐断）
                // 我们在供给 AI 前，强制切断超文本文本
                if (unescaped.Length > 6000)
                {
                    unescaped = unescaped.Substring(0, 6000) + "\n\n...[内容已截断截取边界]...";
                }
                actionResult.OutputData["text"] = unescaped;
            }
            else if (extractType == "rect")
            {
                // Remove JSON string escaping added by WebView2
                var cleanJson = System.Text.RegularExpressions.Regex.Unescape(resultStr.Trim('"'));
                var rectObj = JObject.Parse(cleanJson);
                actionResult.OutputData["rect"] = rectObj;
                _logger.LogDebug("Extracted Rect for {Selector}: {Rect}", selector, cleanJson);
            }

            return actionResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DOM Perception failed for selector {Selector}", selector);
            return ActionResult.Fail($"DOM execution error: {ex.Message}");
        }
    }
}
