using System.Collections.Generic;

namespace KSPClub
{
    /// <summary>
    /// ScenarioModule tracking which vessel persistentIds and Kerbal names
    /// belong to this player. Saved inside persistent.sfs as KSPClubScenario.
    /// The merger keeps this block with the player's persistent layer.
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

        private readonly HashSet<uint>   _ownedVesselIds    = new HashSet<uint>();
        private readonly HashSet<string> _ownedKerbalNames  = new HashSet<string>();

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
            ConfigNode vessels = node.AddNode("OWNED_VESSELS");
            foreach (uint id in _ownedVesselIds)
                vessels.AddValue("id", id.ToString());

            ConfigNode kerbals = node.AddNode("OWNED_KERBALS");
            foreach (string name in _ownedKerbalNames)
                kerbals.AddValue("name", name);
        }

        public override void OnLoad(ConfigNode node)
        {
            _ownedVesselIds.Clear();
            ConfigNode? vessels = node.GetNode("OWNED_VESSELS");
            if (vessels != null)
                foreach (string idStr in vessels.GetValues("id"))
                    if (uint.TryParse(idStr, out uint id))
                        _ownedVesselIds.Add(id);

            _ownedKerbalNames.Clear();
            ConfigNode? kerbals = node.GetNode("OWNED_KERBALS");
            if (kerbals != null)
                foreach (string name in kerbals.GetValues("name"))
                    if (!string.IsNullOrEmpty(name))
                        _ownedKerbalNames.Add(name);
        }

        // ------------------------------------------------------------------ vessel ownership

        public void ClaimVessel(uint persistentId)
        {
            if (_ownedVesselIds.Add(persistentId))
                UnityEngine.Debug.Log($"[KSPClub] Claimed vessel persistentId={persistentId}");
        }

        public void ReleaseVessel(uint persistentId)
        {
            if (_ownedVesselIds.Remove(persistentId))
                UnityEngine.Debug.Log($"[KSPClub] Released vessel persistentId={persistentId}");
        }

        public bool OwnsVessel(uint persistentId) => _ownedVesselIds.Contains(persistentId);

        public int OwnedVesselCount => _ownedVesselIds.Count;

        // ------------------------------------------------------------------ Kerbal ownership

        public void ClaimKerbal(string name)
        {
            if (_ownedKerbalNames.Add(name))
                UnityEngine.Debug.Log($"[KSPClub] Claimed Kerbal '{name}'");
        }

        public bool OwnsKerbal(string name) => _ownedKerbalNames.Contains(name);

        public int OwnedKerbalCount => _ownedKerbalNames.Count;
    }
}
