namespace PlayerAssistant
{
    /// <summary>
    /// The immutable authorization result produced by XP password validation.
    /// Display names and aliases are presentation conveniences; CanonicalId is the
    /// stable identity that protected callers must carry forward.
    /// </summary>
    internal sealed record XpAuthenticatedIdentity(
        string CanonicalId,
        string CanonicalName,
        IReadOnlyList<string> Aliases,
        bool IsDungeonMaster,
        string AccountScope);
}
