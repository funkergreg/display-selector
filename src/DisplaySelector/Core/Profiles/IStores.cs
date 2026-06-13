namespace DisplaySelector.Core.Profiles;

/// <summary>Loads/saves the profile collection. Never throws on read — recovers or starts empty.</summary>
public interface IProfileStore
{
    ProfilesDocument Load();

    void Save(ProfilesDocument document);
}

/// <summary>Loads/saves app settings. Never throws on read — recovers or returns defaults.</summary>
public interface IConfigStore
{
    AppConfig Load();

    void Save(AppConfig config);
}
