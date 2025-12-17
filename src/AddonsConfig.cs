namespace AddonsManager.Config;

public class AddonsConfig
{
    public List<string> Addons { get; set; } = [];
    public bool BlockDisconnectMessages { get; set; } = true;
    public bool CacheClientsWithAddons { get; set; } = true;
    public float CacheClientsDurationInSeconds { get; set; } = 0.0f;
    public float ExtraAddonsTimeoutInSeconds { get; set; } = 10.0f;
};