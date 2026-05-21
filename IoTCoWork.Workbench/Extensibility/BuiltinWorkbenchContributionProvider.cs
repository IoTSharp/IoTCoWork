namespace IoTCoWork.Workbench.Extensibility;

public sealed class BuiltinWorkbenchContributionProvider : IWorkbenchContributionProvider
{
    public IEnumerable<WorkbenchContribution> GetContributions(WorkbenchContributionContext context)
    {
        foreach (var contribution in GetNavigation())
        {
            yield return contribution;
        }

        foreach (var contribution in GetContextTabs(context))
        {
            yield return contribution;
        }

        foreach (var contribution in GetSettingsCategories())
        {
            yield return contribution;
        }

        foreach (var contribution in GetSenderContextChips(context))
        {
            yield return contribution;
        }
    }

    private static IEnumerable<NavigationContribution> GetNavigation()
    {
        yield return new("sessions", "会话", "本地任务会话列表。", "message", 10)
        {
            Group = "workspace",
            IsPrimary = true,
        };
        yield return new("capabilities", "能力中心", "本地智能体、技能、MCP 与插件占位。", "appstore", 20)
        {
            Group = "workspace",
            ActionId = "open-capabilities",
        };
    }

    private static IEnumerable<ContextTabContribution> GetContextTabs(WorkbenchContributionContext context)
    {
        yield return new("tree", "工程树", "本地 workspace 结构。", "folder-open", 10)
        {
            Kind = "builtin",
        };
        yield return new("artifacts", "产物", "当前会话产物摘要。", "file-done", 20)
        {
            Badge = context.ArtifactCount > 0 ? context.ArtifactCount.ToString() : null,
            Kind = "builtin",
        };
        yield return new("generation", "生成任务", "SaaS Workspace 生成任务状态。", "cloud-sync", 30)
        {
            Badge = context.IsRunning ? "运行" : null,
            Kind = "builtin",
        };
        yield return new("tools", "工具运行", "本地任务计划与工具运行。", "tool", 40)
        {
            Badge = context.IsRunning ? "运行" : null,
            Kind = "builtin",
        };
        yield return new("logs", "日志", "本地工作台日志。", "profile", 50)
        {
            Kind = "builtin",
        };
        yield return new("risk", "风险", "边界与安全风险。", "safety", 60)
        {
            Badge = "2",
            Kind = "builtin",
        };
    }

    private static IEnumerable<SettingsCategoryContribution> GetSettingsCategories()
    {
        yield return new("appearance", "外观", "主题与界面显示。", "skin", 10)
        {
            Keywords = ["theme", "appearance", "外观", "主题"],
        };
        yield return new("shortcuts", "快捷键", "本地键盘入口。", "keyboard", 20)
        {
            Keywords = ["shortcut", "hotkey", "快捷键", "键盘"],
        };
        yield return new("model", "模型", "作图模型与请求参数。", "experiment", 30)
        {
            Keywords = ["model", "image", "模型", "质量", "格式"],
        };
        yield return new("network", "本地网络", "本机代理与连接状态。", "global", 40)
        {
            Keywords = ["network", "proxy", "网络", "代理"],
        };
        yield return new("updates", "更新", "应用版本检查。", "cloud-sync", 50)
        {
            Keywords = ["update", "release", "更新", "版本"],
        };
        yield return new("capabilities", "能力中心", "本地扩展点入口。", "appstore", 60)
        {
            Keywords = ["capability", "plugin", "mcp", "能力", "插件"],
        };
    }

    private static IEnumerable<SenderContextChipContribution> GetSenderContextChips(WorkbenchContributionContext context)
    {
        yield return new("workspace", "工作区", "当前本地 workspace。", "folder-open", 10)
        {
            Value = context.WorkspaceName,
            IsStatic = true,
        };
        yield return new("edge-target", "边缘", "边缘目标端。", "thunderbolt", 20)
        {
            Value = "C# AOT",
            ActionId = "cycle-edge-target",
        };
        yield return new("model", "模型", "当前模型。", "experiment", 30)
        {
            Value = context.Model,
            ActionId = "cycle-model",
        };
        yield return new("approval", "审批", "本地审批模式。", "safety", 40)
        {
            Value = "每次审批",
            ActionId = "cycle-approval",
        };
        yield return new("output", "输出", "本地输出位置。", "file-done", 50)
        {
            Value = "当前会话",
            ActionId = "cycle-output",
        };
    }
}
