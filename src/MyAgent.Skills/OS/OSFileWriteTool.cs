using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyAgent.Core.Attributes;
using MyAgent.Core.Interfaces;
using MyAgent.Core.Models;
using Newtonsoft.Json.Linq;

namespace MyAgent.Skills.OS;

[SkillAction("os.file_write")]
public class OSFileWriteTool : IActionTool
{
    private readonly ILogger<OSFileWriteTool> _logger;

    public string ActionType => "os.file_write";

    public OSFileWriteTool(ILogger<OSFileWriteTool> logger)
    {
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(SkillExecutionContext context, JToken parameters, CancellationToken cancellationToken)
    {
        string? path = parameters["path"]?.ToString();
        string? content = parameters["content"]?.ToString();

        if (string.IsNullOrEmpty(path))
            return ActionResult.Fail("Missing required parameter: path");

        if (content == null) content = string.Empty;

        try
        {
            // Normalize path relative to application root
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string fullPath = Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path);

            // Ensure directory exists
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(fullPath, content, cancellationToken);
            _logger.LogInformation("Successfully wrote to file: {Path}", fullPath);
            
            var result = ActionResult.Success();
            result.OutputData["written_path"] = fullPath;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write file {Path}", path);
            return ActionResult.Fail($"File write error: {ex.Message}");
        }
    }
}
