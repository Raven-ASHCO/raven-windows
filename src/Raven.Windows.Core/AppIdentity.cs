namespace Raven.Windows.Core;

/// <summary>
/// Raven's one app id (desktop/README.md, "App identity"). On Windows it is the MSIX
/// Identity Name, the AppUserModelID of the unpackaged app and the single-instance key.
/// macOS uses the same spelling; Linux spells it io.github.raven_ashco.Raven (Flathub's rule).
/// </summary>
public static class AppIdentity
{
    public const string Id = "io.github.raven-ashco.Raven";
}
