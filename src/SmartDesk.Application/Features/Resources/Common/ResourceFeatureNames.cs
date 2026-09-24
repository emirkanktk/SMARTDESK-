namespace SmartDesk.Application.Features.Resources.Common;

// "TV", "tv " and "Tv" are the same feature.
public static class ResourceFeatureNames
{
    public static IReadOnlyList<string> Normalize(IReadOnlyList<string>? features) =>
        (features ?? [])
            .Select(f => f.Trim())
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static bool HasNoDuplicates(IReadOnlyList<string> features) =>
        features.Select(f => f.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == features.Count;
}
