namespace AddonsManager.Contract;

public interface IAddonsManagerApi
{
    public const string Key = "AddonsManager.Api";

    /// <summary>Workshop addons from the config file plus those added through this API.</summary>
    IReadOnlyList<string> GetAddons();

    /// <summary>Workshop addons currently mounted on the server.</summary>
    IReadOnlyList<string> GetMountedAddons();

    /// <summary>
    /// Registers a workshop addon, then mounts it (or downloads it first, reloading the map when done).
    /// Not persisted: plugins must call this on every load. Returns false if the ID is invalid or already registered.
    /// </summary>
    bool AddAddon(string workshopId);

    /// <summary>Unregisters and unmounts an addon added through <see cref="AddAddon"/>. Config addons cannot be removed.</summary>
    bool RemoveAddon(string workshopId);
}
