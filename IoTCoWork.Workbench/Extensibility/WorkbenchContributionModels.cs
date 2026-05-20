namespace IoTCoWork.Workbench.Extensibility;

public interface IWorkbenchContributionProvider
{
    IEnumerable<WorkbenchContribution> GetContributions(WorkbenchContributionContext context);
}

public sealed record WorkbenchContributionContext(
    string WorkspaceName,
    string ActiveSessionTitle,
    string Mode,
    string WorkspaceStatus,
    string Model,
    string TargetSize,
    int ArtifactCount,
    bool IsRunning);

public abstract record WorkbenchContribution(
    string Id,
    string Title,
    string Description,
    string Icon,
    int Order = 0)
{
    public string Source { get; init; } = "builtin";
    public string? Group { get; init; }
}

public sealed record NavigationContribution(
    string Id,
    string Title,
    string Description,
    string Icon,
    int Order = 0)
    : WorkbenchContribution(Id, Title, Description, Icon, Order)
{
    public string? ActionId { get; init; }
    public bool IsPrimary { get; init; }
}

public sealed record ContextTabContribution(
    string Id,
    string Title,
    string Description,
    string Icon,
    int Order = 0)
    : WorkbenchContribution(Id, Title, Description, Icon, Order)
{
    public string? Badge { get; init; }
    public string Kind { get; init; } = "summary";
}

public sealed record SettingsCategoryContribution(
    string Id,
    string Title,
    string Description,
    string Icon,
    int Order = 0)
    : WorkbenchContribution(Id, Title, Description, Icon, Order)
{
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public string Status { get; init; } = string.Empty;
    public string Kind { get; init; } = "builtin";

    public bool Matches(string query)
    {
        return Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Keywords.Any(keyword => keyword.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record SenderContextChipContribution(
    string Id,
    string Title,
    string Description,
    string Icon,
    int Order = 0)
    : WorkbenchContribution(Id, Title, Description, Icon, Order)
{
    public string Value { get; init; } = string.Empty;
    public string? ActionId { get; init; }
    public bool IsStatic { get; init; }
}
