using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyAgent.Core.Attributes;
using MyAgent.Core.Interfaces;
using MyAgent.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MyAgent.Skills.AI;

[SkillAction("ai.analyze")]
public class AiAnalyzeTool : IActionTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiAnalyzeTool> _logger;

    public string ActionType => "ai.analyze";

    public AiAnalyzeTool(IHttpClientFactory httpClientFactory, ILogger<AiAnalyzeTool> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(SkillExecutionContext context, JToken parameters, CancellationToken cancellationToken)
    {
        // 从参数或上下文中读取配置，支持前台 UI 实时覆盖
        string prompt = parameters["prompt"]?.ToString() ?? string.Empty;
        
        string model = context.EnvironmentArgs.TryGetValue("AiModelName", out var m) && !string.IsNullOrWhiteSpace(m?.ToString())
            ? m.ToString()! 
            : parameters["model"]?.ToString() ?? "qwen-plus";

        string apiKey = context.EnvironmentArgs.TryGetValue("AiApiKey", out var key) && !string.IsNullOrWhiteSpace(key?.ToString())
            ? key.ToString()! 
            : Environment.GetEnvironmentVariable("AI_API_KEY") ?? string.Empty;

        string baseUrl = context.EnvironmentArgs.TryGetValue("AiBaseUrl", out var url) && !string.IsNullOrWhiteSpace(url?.ToString())
            ? url.ToString()! 
            : parameters["base_url"]?.ToString() ?? "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";

        if (string.IsNullOrEmpty(apiKey))
            return ActionResult.Fail("Missing AI API Key in configuration or environment.");

        if (string.IsNullOrEmpty(prompt))
            return ActionResult.Fail("Missing parameter: prompt");

        try
        {
            var requestBody = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            string jsonContent = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient("AiModelsClient");
            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = content;

            // 让前端确切知道目前进入了耗时较长的大模型思考期
            _logger.LogInformation("🧠 AI is thinking... (Model: {Model})", model);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var response = await client.SendAsync(request, cancellationToken);
            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("AI API call failed after {ElapsedMs}ms: {Status} - {Response}", sw.ElapsedMilliseconds, response.StatusCode, responseString);
                return ActionResult.Fail($"AI Request failed: {response.StatusCode} {responseString}");
            }

            _logger.LogInformation("💡 AI responded in {ElapsedMs}ms.", sw.ElapsedMilliseconds);

            var jsonResult = JObject.Parse(responseString);
            var replyMessage = jsonResult["choices"]?[0]?["message"]?["content"]?.ToString();

            if (string.IsNullOrEmpty(replyMessage))
                return ActionResult.Fail("Failed to parse API response or it was empty.");

            var actionResult = ActionResult.Success();
            
            actionResult.OutputData["analysis_text"] = replyMessage;
            try
            {
                string cleanJson = replyMessage.Trim();
                // 优先尝试从标准的 ```json 代码块中提取
                var match = System.Text.RegularExpressions.Regex.Match(cleanJson, @"```json\s*(.*?)\s*```", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (match.Success)
                {
                    cleanJson = match.Groups[1].Value.Trim();
                }
                else
                {
                    // 兼容 AI 没写开头或者结尾的极端情况
                    cleanJson = cleanJson.Replace("```json", "").Replace("```", "").Trim();
                }

                if (cleanJson.StartsWith("{") || cleanJson.StartsWith("["))
                {
                    var parsed = JToken.Parse(cleanJson);
                    actionResult.OutputData["analysis_json"] = parsed;
                }
            }
            catch { /* Ignored */ }

            return actionResult;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "AI API Request Timed Out (TaskCanceled)");
            return ActionResult.Fail("AI 请求超时或连接被断开 (Timeout)。请检查您的网络连通性、API BaseUrl 是否正确，或者您的本地是否开启了合适的全局系统级代理以访问海外模型节点。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI analysis request failed");
            return ActionResult.Fail($"AI Request exception: {ex.Message}");
        }
    }
}
