namespace KSPClub
{
    /// <summary>
    /// Diplomatic stance toward another player's agency.
    /// Friendly  — full orbit visibility, future CommNet relay sharing
    /// Neutral   — visible but dimmed orbits (default for everyone)
    /// Hostile   — heavily dimmed orbits, future CommNet blocked
    /// </summary>
    public enum Relation
    {
        Friendly,
        Neutral,
        Hostile,
    }
}
