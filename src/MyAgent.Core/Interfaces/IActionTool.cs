using System.Threading;
using System.Threading.Tasks;
using MyAgent.Core.Models;
using Newtonsoft.Json.Linq;

namespace MyAgent.Core.Interfaces;

public interface IActionTool
{
    /// <summary>
    /// 工具的唯一标识符，例如 "browser.navigate" 或 "os.mouse"
    /// 配置文件将通过此名称映射到对应的类实现
    /// </summary>
    string ActionType { get; }
    
    /// <summary>
    /// 核心执行逻辑，必须支持 CancellationToken
    /// JToken 允许传入动态结构，工具内部在使用前映射为强类型的 DTO
    /// </summary>
    Task<ActionResult> ExecuteAsync(SkillExecutionContext context, JToken parameters, CancellationToken cancellationToken);
}
