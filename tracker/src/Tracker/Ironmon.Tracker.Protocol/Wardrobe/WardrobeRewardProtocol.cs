namespace Ironmon.Tracker.Protocol.Wardrobe;

/// <summary>
/// Defines durable future-achievement rewards for the game's shared wardrobe profile.
/// </summary>
public static class WardrobeRewardProtocol
{
    /// <summary>
    /// Gets the independently negotiated reward delivery version.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// Gets the bounded number of earned identities in one delivery.
    /// </summary>
    public const int MaximumIds = 4096;

    /// <summary>
    /// Gets the event delivering future unlocks that have not yet been credited.
    /// </summary>
    public const string Delivery = "wardrobe_achievement_rewards";

    /// <summary>
    /// Gets the event acknowledging the game's durable currency commit.
    /// </summary>
    public const string Acknowledged = "wardrobe_achievement_rewards_acknowledged";
}
