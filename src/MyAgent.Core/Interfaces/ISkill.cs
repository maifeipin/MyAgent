using System.Threading.Tasks;
using MyAgent.Core.Models;

namespace MyAgent.Core.Interfaces;

public interface ISkill
{
    string SkillId { get; }
    string Name { get; }
    
    // 执行当前技能，系统自动装配执行路径和所需的子 ActionTool
    Task<ActionResult> ExecuteAsync(SkillExecutionContext context);
}
