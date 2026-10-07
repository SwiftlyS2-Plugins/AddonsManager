using AddonsManager.Contract;
using AddonsManager.SteamWorkshop;

namespace AddonsManager.Api;

public class AddonsApi : IAddonsManagerApi
{
    public AddonsWorkshopManager? Manager;

    public IReadOnlyList<string> GetAddons() => Manager?.GetAllAddons() ?? [];

    public IReadOnlyList<string> GetMountedAddons() => Manager?.GetMountedAddonsSnapshot() ?? [];

    public bool AddAddon(string workshopId) => Manager?.AddExtraAddon(workshopId) ?? false;

    public bool RemoveAddon(string workshopId) => Manager?.RemoveExtraAddon(workshopId) ?? false;
}
