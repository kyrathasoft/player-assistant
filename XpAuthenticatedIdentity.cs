namespace PlayerAssistant
{
    internal enum XpIdentityRole
    {
        Player,
        DungeonMaster
    }

    /// <summary>Canonical identity record loaded from the validated identity registry.</summary>
    internal sealed record XpCanonicalIdentityRecord(
        string CanonicalId,
        string CanonicalName,
        IReadOnlyList<string> Aliases,
        XpIdentityRole Role);

    /// <summary>
    /// Immutable authorization result. Role and scope are derived only by the factory;
    /// callers cannot provide either independently.
    /// </summary>
    internal sealed record XpAuthenticatedIdentity
    {
        public const string DungeonMasterScope = "*";

        private XpAuthenticatedIdentity(XpCanonicalIdentityRecord record)
        {
            CanonicalId = record.CanonicalId;
            CanonicalName = record.CanonicalName;
            Aliases = record.Aliases.ToArray();
            IsDungeonMaster = record.Role == XpIdentityRole.DungeonMaster;
            AccountScope = IsDungeonMaster ? DungeonMasterScope : record.CanonicalId;
        }

        public string CanonicalId { get; }
        public string CanonicalName { get; }
        public IReadOnlyList<string> Aliases { get; }
        public bool IsDungeonMaster { get; }
        public string AccountScope { get; }

        internal static XpAuthenticatedIdentity Create(XpCanonicalIdentityRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentException.ThrowIfNullOrWhiteSpace(record.CanonicalId);
            ArgumentException.ThrowIfNullOrWhiteSpace(record.CanonicalName);
            ArgumentNullException.ThrowIfNull(record.Aliases);
            if (!Enum.IsDefined(record.Role))
            {
                throw new ArgumentException("The canonical identity role is invalid.", nameof(record));
            }

            return new XpAuthenticatedIdentity(record);
        }
    }
}
