namespace DisplaySelector.App;

/// <summary>
/// Which profile the Profile Manager list should select after the profile list changed. Pure, so the
/// rules are unit-tested: a newly added profile wins; otherwise the selection follows its profile by id
/// (so "Move up" can be pressed repeatedly); if that profile is gone, select the same position, clamped.
/// </summary>
internal static class ProfileListSelection
{
    public static string? Next(IReadOnlyList<string> oldIds, string? selectedId, IReadOnlyList<string> newIds)
    {
        if (newIds.Count == 0)
        {
            return null;
        }

        var added = newIds.Where(id => !oldIds.Contains(id)).ToList();
        if (added.Count == 1)
        {
            return added[0];
        }

        if (selectedId is not null)
        {
            if (newIds.Contains(selectedId))
            {
                return selectedId;
            }

            for (var oldIndex = 0; oldIndex < oldIds.Count; oldIndex++)
            {
                if (oldIds[oldIndex] == selectedId)
                {
                    return newIds[Math.Min(oldIndex, newIds.Count - 1)];
                }
            }
        }

        return newIds[0];
    }
}
