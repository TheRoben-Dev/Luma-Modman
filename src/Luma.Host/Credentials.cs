namespace Luma.Host;

/// <summary>The player's own API keys for the mod sites. Kept in a file only they can read.</summary>
public sealed record Credentials
{
    public string? Nexus { get; init; }
    public string? NexusUser { get; init; }
    public bool NexusPremium { get; init; }
    public string? CurseForge { get; init; }
    public string? Modio { get; init; }
}

public sealed class CredentialStore
{
    private readonly string _file;

    public Credentials Current { get; private set; }

    public CredentialStore(string file)
    {
        _file = file;
        Current = AppHost.LoadJson<Credentials>(file) ?? new Credentials();
    }

    public void Save(Credentials c)
    {
        Current = c;
        AppHost.SaveJson(_file, c);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
