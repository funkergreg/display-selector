using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Launch;

/// <summary>
/// Resolves a shortcut's <c>--profile</c> reference. Id first (what the app writes — survives renames),
/// then a case-insensitive name (what a person types into a hand-made shortcut).
/// </summary>
public static class ProfileResolver
{
    public static Profile? Resolve(IReadOnlyList<Profile> profiles, string reference)
    {
        return profiles.FirstOrDefault(p => string.Equals(p.Id, reference, StringComparison.OrdinalIgnoreCase))
            ?? profiles.FirstOrDefault(p => string.Equals(p.Name, reference, StringComparison.OrdinalIgnoreCase));
    }
}
