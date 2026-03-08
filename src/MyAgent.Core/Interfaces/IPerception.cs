using System.Threading.Tasks;

namespace MyAgent.Core.Interfaces;

public enum PerceptionMethod
{
    DOM,
    OCR,
    VL
}

public interface IPerception
{
    PerceptionMethod Method { get; }
    
    // 执行屏幕感知，通过多模态或节点解析提取内容
    Task<object> ExtractAsync(string target, string rules);
}
