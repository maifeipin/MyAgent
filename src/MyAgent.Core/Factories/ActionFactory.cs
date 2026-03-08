using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyAgent.Core.Attributes;
using MyAgent.Core.Interfaces;

namespace MyAgent.Core.Factories;

public interface IActionFactory
{
    IActionTool? CreateAction(string actionName);
    void RegisterActionsFromAssembly(Assembly assembly);
}

public class ActionFactory : IActionFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ActionFactory> _logger;
    private readonly ConcurrentDictionary<string, Type> _actionTypes = new();

    public ActionFactory(IServiceProvider serviceProvider, ILogger<ActionFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public void RegisterActionsFromAssembly(Assembly assembly)
    {
        var types = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IActionTool).IsAssignableFrom(t));

        foreach (var type in types)
        {
            var attribute = type.GetCustomAttribute<SkillActionAttribute>();
            if (attribute != null)
            {
                if (_actionTypes.TryAdd(attribute.ActionName, type))
                {
                    _logger.LogDebug("Registered ActionTool: {ActionName} -> {TypeName}", attribute.ActionName, type.Name);
                }
                else
                {
                    _logger.LogWarning("ActionTool duplicated registration attempt for: {ActionName}", attribute.ActionName);
                }
            }
        }
    }

    public IActionTool? CreateAction(string actionName)
    {
        if (_actionTypes.TryGetValue(actionName, out var type))
        {
            // 通过 DI 容器中的 ActivatorUtilities 反射实例化，允许工具类的构造函数注入其他 Service
            return (IActionTool)ActivatorUtilities.CreateInstance(_serviceProvider, type);
        }

        _logger.LogError("ActionTool not found for action name: {ActionName}", actionName);
        return null;
    }
}
