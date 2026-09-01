namespace PlayerAssistant
{
    /// <summary>Canonical authorization predicates shared by protected desktop services.</summary>
    internal static class AuthorizationPolicy
    {
        internal static bool CanReadOwnedResource(
            XpAuthenticatedIdentity? identity,
            string? resourceOwnerCanonicalId)
        {
            return identity is not null
                && !string.IsNullOrWhiteSpace(resourceOwnerCanonicalId)
                && string.Equals(identity.CanonicalId, resourceOwnerCanonicalId, StringComparison.Ordinal);
        }

        internal static bool CanReadDungeonMasterResource(XpAuthenticatedIdentity? identity)
        {
            return identity?.IsDungeonMaster == true
                && identity.AccountScope == XpAuthenticatedIdentity.DungeonMasterScope;
        }
    }
}
