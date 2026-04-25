using System.Collections.Generic;

namespace KSPClub
{
    /// <summary>
    /// ScenarioModule that tracks which vessel persistentIds belong to this player.
    /// Saved and loaded automatically with the game — the data lives inside
    /// persistent.sfs under a SCENARIO { name = KSPClubScenario } block.
    ///
    /// The merger tool recognises "KSPClubScenario" as a persistent scenario
    /// and keeps it with the player's save across weekly merges.
    /// </summary>
    [KSPScenario(
        ScenarioCreationOptions.AddToAllGames | ScenarioCreationOptions.AddToExistingGames,
        GameScenes.FLIGHT,
        GameScenes.TRACKSTATION,
        GameScenes.SPACECENTER,
        GameScenes.EDITOR
    )]
    public class KSPClubScenario : ScenarioModule
    {
        public static KSPClubScenario? Instance { get; private set; }

        private readonly HashSet<uint> _ownedIds = new HashSet<uint>();

        // ------------------------------------------------------------------ lifecycle

        public override void OnAwake()
        {
            base.OnAwake();
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ save / load

        public override void OnSave(ConfigNode node)
        {
            ConfigNode owned = node.AddNode("OWNED_VESSELS");
            foreach (uint id in _ownedIds)
                owned.AddValue("id", id.ToString());
        }

        public override void OnLoad(ConfigNode node)
        {
            _ownedIds.Clear();
            ConfigNode? owned = node.GetNode("OWNED_VESSELS");
            if (owned == null) return;
            foreach (string idStr in owned.GetValues("id"))
                if (uint.TryParse(idStr, out uint id))
                    _ownedIds.Add(id);
        }

        // ------------------------------------------------------------------ ownership

        public void ClaimVessel(uint persistentId)
        {
            _ownedIds.Add(persistentId);
            UnityEngine.Debug.Log($"[KSPClub] Claimed vessel persistentId={persistentId}");
        }

        public bool OwnsVessel(uint persistentId) => _ownedIds.Contains(persistentId);

        public int OwnedCount => _ownedIds.Count;
    }
}
