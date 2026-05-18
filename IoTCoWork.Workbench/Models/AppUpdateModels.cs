namespace IoTCoWork.Workbench.Models;

public sealed record AppUpdateCheckResponse(
    string CurrentVersion,
    string CurrentVersionDisplay,
    string Repository,
    string Platform,
    bool Supported,
    bool CanInstall,
    bool UpdateAvailable,
    string? LatestVersion,
    string? LatestVersionDisplay,
    string? LatestTagName,
    string? ReleaseName,
    string? ReleaseUrl,
    DateTimeOffset? PublishedAt,
    AppUpdateAssetInfo? Asset,
    string Message);

public sealed record AppUpdateAssetInfo(
    string Name,
    long Size);

public sealed record AppUpdateInstallRequest(
    string? TagName,
    string? AssetName);

public sealed record AppUpdateInstallResponse(
    string Status,
    string Message);

public static class AppUpdateVersionComparer
{
    public static bool IsNewer(string? candidateVersion, string? currentVersion) =>
        Compare(candidateVersion, currentVersion) > 0;

    public static int Compare(string? left, string? right)
    {
        if (TryParse(left, out var leftVersion) &&
            TryParse(right, out var rightVersion))
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return string.Compare(
            NormalizeForDisplay(left),
            NormalizeForDisplay(right),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string ToDisplayVersion(string? version)
    {
        var normalized = NormalizeForDisplay(version);
        return normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"v{normalized}";
    }

    public static string NormalizeForDisplay(string? version)
    {
        var value = (version ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return "0.0.0";
        }

        var metadataIndex = value.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            value = value[..metadataIndex];
        }

        return value.Length == 0 ? "0.0.0" : value;
    }

    private static bool TryParse(string? value, out ParsedVersion version)
    {
        version = default;
        var normalized = NormalizeForDisplay(value);
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        if (normalized.Length == 0)
        {
            return false;
        }

        var prereleaseIndex = normalized.IndexOf('-', StringComparison.Ordinal);
        var core = prereleaseIndex >= 0 ? normalized[..prereleaseIndex] : normalized;
        var prerelease = prereleaseIndex >= 0 ? normalized[(prereleaseIndex + 1)..] : string.Empty;
        var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var numbers = new int[Math.Max(3, parts.Length)];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], out var number) || number < 0)
            {
                return false;
            }

            numbers[index] = number;
        }

        var prereleaseParts = string.IsNullOrWhiteSpace(prerelease)
            ? []
            : prerelease.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        version = new ParsedVersion(numbers, prereleaseParts);
        return true;
    }

    private readonly record struct ParsedVersion(
        int[] Numbers,
        string[] PrereleaseParts) : IComparable<ParsedVersion>
    {
        public int CompareTo(ParsedVersion other)
        {
            var length = Math.Max(Numbers.Length, other.Numbers.Length);
            for (var index = 0; index < length; index++)
            {
                var left = index < Numbers.Length ? Numbers[index] : 0;
                var right = index < other.Numbers.Length ? other.Numbers[index] : 0;
                var numberComparison = left.CompareTo(right);
                if (numberComparison != 0)
                {
                    return numberComparison;
                }
            }

            if (PrereleaseParts.Length == 0 && other.PrereleaseParts.Length == 0)
            {
                return 0;
            }

            if (PrereleaseParts.Length == 0)
            {
                return 1;
            }

            if (other.PrereleaseParts.Length == 0)
            {
                return -1;
            }

            var prereleaseLength = Math.Max(PrereleaseParts.Length, other.PrereleaseParts.Length);
            for (var index = 0; index < prereleaseLength; index++)
            {
                if (index >= PrereleaseParts.Length)
                {
                    return -1;
                }

                if (index >= other.PrereleaseParts.Length)
                {
                    return 1;
                }

                var comparison = ComparePrereleasePart(
                    PrereleaseParts[index],
                    other.PrereleaseParts[index]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        private static int ComparePrereleasePart(string left, string right)
        {
            var leftIsNumeric = int.TryParse(left, out var leftNumber);
            var rightIsNumeric = int.TryParse(right, out var rightNumber);
            if (leftIsNumeric && rightIsNumeric)
            {
                return leftNumber.CompareTo(rightNumber);
            }

            if (leftIsNumeric)
            {
                return -1;
            }

            if (rightIsNumeric)
            {
                return 1;
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
