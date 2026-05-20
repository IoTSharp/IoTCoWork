namespace IoTCoWork.Workbench.Extensibility;

public sealed class WorkbenchContributionCatalog
{
    private readonly IEnumerable<IWorkbenchContributionProvider> _providers;

    public WorkbenchContributionCatalog(IEnumerable<IWorkbenchContributionProvider> providers)
    {
        _providers = providers;
    }

    public IReadOnlyList<NavigationContribution> GetNavigation(WorkbenchContributionContext context) =>
        GetContributions<NavigationContribution>(context);

    public IReadOnlyList<ContextTabContribution> GetContextTabs(WorkbenchContributionContext context) =>
        GetContributions<ContextTabContribution>(context);

    public IReadOnlyList<SettingsCategoryContribution> GetSettingsCategories(WorkbenchContributionContext context) =>
        GetContributions<SettingsCategoryContribution>(context);

    public IReadOnlyList<SenderContextChipContribution> GetSenderContextChips(WorkbenchContributionContext context) =>
        GetContributions<SenderContextChipContribution>(context);

    private IReadOnlyList<TContribution> GetContributions<TContribution>(WorkbenchContributionContext context)
        where TContribution : WorkbenchContribution
    {
        return _providers
            .SelectMany(provider => provider.GetContributions(context))
            .OfType<TContribution>()
            .OrderBy(contribution => contribution.Order)
            .ThenBy(contribution => contribution.Id, StringComparer.Ordinal)
            .ToArray();
    }
}
