using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.Presentation;

/// <summary>
/// What a consent sheet says before a setting the engine needs a yes for is turned on: what it
/// does, what it costs (bandwidth, disk, being online), and what others can and can't see. Texts
/// from strings.en.json ("network.consent.*"); the engine's own question (texts.&lt;setting&gt;)
/// is shown with them where the engine has one.
/// </summary>
public sealed record ConsentTopic(string Setting, string Title, string Does, string Costs, string Sees, string Hidden)
{
    /// <summary>True when the engine refuses the setting without "agreed": true (CONSENT_NEEDED).</summary>
    public bool EngineNeedsConsent => NetworkSettings.EngineNeedsConsent(Setting);
}

public static class ConsentTopics
{
    /// <summary>
    /// The topic for a setting that is being turned on, or null when it needs no sheet. One
    /// rule in all three apps (API.md): a sheet for exactly the settings the engine refuses
    /// without "agreed": true (carry_for_friends, friends_carry_mine, handoff). Lending a
    /// relay and the bridge, store and relay flags show their effect beside the switch.
    /// </summary>
    public static ConsentTopic? For(string setting) => setting switch
    {
        NetworkSettings.CarryForFriends => new ConsentTopic(setting,
            Loc.Get("network.consent.carryForFriends.title"),
            Loc.Get("network.consent.carryForFriends.does"),
            Loc.Get("network.consent.carryForFriends.costs"),
            Loc.Get("network.consent.carryForFriends.sees"),
            Loc.Get("network.consent.carryForFriends.hidden")),
        NetworkSettings.FriendsCarryMine => new ConsentTopic(setting,
            Loc.Get("network.consent.friendsCarryMine.title"),
            Loc.Get("network.consent.friendsCarryMine.does"),
            Loc.Get("network.consent.friendsCarryMine.costs"),
            Loc.Get("network.consent.friendsCarryMine.sees"),
            Loc.Get("network.consent.friendsCarryMine.hidden")),
        NetworkSettings.Handoff => new ConsentTopic(setting,
            Loc.Get("network.consent.handoff.title"),
            Loc.Get("network.consent.handoff.does"),
            Loc.Get("network.consent.handoff.costs"),
            Loc.Get("network.consent.handoff.sees"),
            Loc.Get("network.consent.handoff.hidden")),
        _ => null,
    };

    /// <summary>
    /// A sheet for a setting the engine asked a yes for (CONSENT_NEEDED) that this app has no
    /// words of its own for (a newer engine): the engine's question carries it.
    /// </summary>
    public static ConsentTopic Generic(string setting)
        => new(setting, Loc.Get("network.consent.generic.title"), string.Empty, string.Empty, string.Empty, string.Empty);

    /// <summary>The engine's question for a setting (NetworkStatus.texts), without the terminal's "[y/N]".</summary>
    public static string EngineText(NetworkStatus? status, string setting)
    {
        if (status is null) return string.Empty;
        string raw = setting switch
        {
            NetworkSettings.CarryForFriends => status.Texts.CarryForFriends,
            NetworkSettings.FriendsCarryMine => status.Texts.FriendsCarryMine,
            NetworkSettings.Handoff => status.Texts.Handoff,
            _ => string.Empty,
        };
        return NetworkPresenter.ConsentText(raw);
    }
}
