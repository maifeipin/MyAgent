using System;

namespace MyAgent.Core.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class SkillActionAttribute : Attribute
{
    public string ActionName { get; }

    public SkillActionAttribute(string actionName)
    {
        ActionName = actionName;
    }
}
