namespace ApeRadar.Models
{
    internal readonly record struct PlayerIdentity(
        string Name,
        string AccountId,
        Server Server,
        string Relation,
        string ShipId)
    {
        public bool HasAccountId => AccountId != "-1";

        public bool MatchesRosterEntry(PlayerIdentity other) =>
            Name == other.Name && Relation == other.Relation && Server == other.Server;

        public bool MatchesAccount(PlayerIdentity other) =>
            Server == other.Server && AccountId == other.AccountId;
    }

    internal readonly record struct PlayerAvailability(
        bool HasAccountId,
        bool IsHidden,
        bool IsDataStale,
        bool IsDataFetchFailed)
    {
        public bool IsUnavailable => !IsHidden && (!HasAccountId || IsDataFetchFailed);

        public bool IsPartial => !HasAccountId || IsDataStale || IsDataFetchFailed;
    }
}
