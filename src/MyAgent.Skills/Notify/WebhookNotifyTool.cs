using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyAgent.Core.Attributes;
using MyAgent.Core.Interfaces;
using MyAgent.Core.Models;
using Newtonsoft.Json.Linq;

namespace MyAgent.Skills.Notify;

[SkillAction("notify.webhook")]
public class WebhookNotifyTool : IActionTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookNotifyTool> _logger;

    public string ActionType => "notify.webhook";

    public WebhookNotifyTool(IHttpClientFactory httpClientFactory, ILogger<WebhookNotifyTool> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(SkillExecutionContext context, JToken parameters, CancellationToken cancellationToken)
    {
        string? enabledStr = parameters["enabled"]?.ToString();
        if (enabledStr != null && (enabledStr.Equals("false", StringComparison.OrdinalIgnoreCase) || enabledStr == "False"))
        {
            _logger.LogInformation("Webhook notify is disabled from settings, skipping step.");
            return ActionResult.Success();
        }

        string? url = parameters["url"]?.ToString();
        string payload = parameters["payload"]?.ToString() ?? string.Empty;

        if (string.IsNullOrEmpty(url))
        {
            _logger.LogWarning("Webhook url is empty, skipping notify step.");
            return ActionResult.Success(); // 宽容机制，若用户未填写则安全跳过
        }

        try
        {
            var client = _httpClientFactory.CreateClient("NotifyClient");
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(url, content, cancellationToken);
            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Webhook delivery failed: {Status} - {Response}", response.StatusCode, responseString);
                return ActionResult.Fail($"Webhook failed: {response.StatusCode}");
            }

            _logger.LogInformation("Webhook payload delivered successfully.");
            return ActionResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Error executing webhook: {Message}", ex.Message);
            return ActionResult.Fail($"Webhook exception: {ex.Message}");
        }
    }
}
