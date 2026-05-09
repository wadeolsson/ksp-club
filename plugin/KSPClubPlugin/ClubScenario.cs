using System.Collections.Generic;

namespace KSPClub
{
    /// <summary>
    /// ScenarioModule persisted inside persistent.sfs as KSPClubScenario.
    /// Tracks vessel/Kerbal ownership, tanker configurations, and pending
    /// fuel-purchase transactions for the merger to process.
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

        private readonly HashSet<uint>   _ownedVesselIds   = new HashSet<uint>();
        private readonly HashSet<string> _ownedKerbalNames = new HashSet<string>();

        // Tanker configs for own vessels
        private readonly Dictionary<uint, TankerConfig>    _tankers      = new Dictionary<uint, TankerConfig>();
        // Fuel-purchase transactions waiting for the merger to process
        private readonly List<TransactionRecord>            _transactions = new List<TransactionRecord>();

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
            // Owned vessels
            ConfigNode ov = node.AddNode("OWNED_VESSELS");
            foreach (uint id in _ownedVesselIds)
                ov.AddValue("id", id.ToString());

            // Owned Kerbals
            ConfigNode ok = node.AddNode("OWNED_KERBALS");
            foreach (string name in _ownedKerbalNames)
                ok.AddValue("name", name);

            // Tanker configs
            if (_tankers.Count > 0)
            {
                ConfigNode tc = node.AddNode("TANKER_CONFIGS");
                foreach (var kv in _tankers)
                {
                    ConfigNode t = tc.AddNode("TANKER");
                    t.AddValue("persistentId", kv.Key.ToString());
                    kv.Value.Save(t);
                }
            }

            // Pending transactions
            if (_transactions.Count > 0)
            {
                ConfigNode txs = node.AddNode("TRANSACTIONS");
                foreach (var tx in _transactions)
                {
                    ConfigNode t = txs.AddNode("TX");
                    tx.Save(t);
                }
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            // Owned vessels
            _ownedVesselIds.Clear();
            ConfigNode? ov = node.GetNode("OWNED_VESSELS");
            if (ov != null)
                foreach (string s in ov.GetValues("id"))
                    if (uint.TryParse(s, out uint id))
                        _ownedVesselIds.Add(id);

            // Owned Kerbals
            _ownedKerbalNames.Clear();
            ConfigNode? ok = node.GetNode("OWNED_KERBALS");
            if (ok != null)
                foreach (string name in ok.GetValues("name"))
                    if (!string.IsNullOrEmpty(name))
                        _ownedKerbalNames.Add(name);

            // Tanker configs
            _tankers.Clear();
            ConfigNode? tc = node.GetNode("TANKER_CONFIGS");
            if (tc != null)
                foreach (ConfigNode t in tc.GetNodes("TANKER"))
                    if (uint.TryParse(t.GetValue("persistentId"), out uint pid))
                        _tankers[pid] = TankerConfig.Load(t);

            // Pending transactions
            _transactions.Clear();
            ConfigNode? txs = node.GetNode("TRANSACTIONS");
            if (txs != null)
                foreach (ConfigNode t in txs.GetNodes("TX"))
                    _transactions.Add(TransactionRecord.Load(t));
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
        public int  OwnedVesselCount              => _ownedVesselIds.Count;

        // ------------------------------------------------------------------ Kerbal ownership

        public void ClaimKerbal(string name)
        {
            if (_ownedKerbalNames.Add(name))
                UnityEngine.Debug.Log($"[KSPClub] Claimed Kerbal '{name}'");
        }

        public bool OwnsKerbal(string name) => _ownedKerbalNames.Contains(name);
        public int  OwnedKerbalCount        => _ownedKerbalNames.Count;

        // ------------------------------------------------------------------ tanker management

        public void SetTankerConfig(uint persistentId, TankerConfig config)
        {
            _tankers[persistentId] = config;
            UnityEngine.Debug.Log($"[KSPClub] Tanker config saved for pid={persistentId} active={config.Active}");
        }

        public TankerConfig? GetTankerConfig(uint persistentId)
            => _tankers.TryGetValue(persistentId, out var c) ? c : null;

        public bool IsTanker(uint persistentId)
            => _tankers.TryGetValue(persistentId, out var c) && c.Active;

        // ------------------------------------------------------------------ transactions

        public void RecordTransaction(TransactionRecord tx)
        {
            _transactions.Add(tx);
            UnityEngine.Debug.Log($"[KSPClub] Transaction recorded: {tx.Buyer} paid ◆{tx.TotalCost:F0} to {tx.Seller} for {tx.Amount:F1}u {tx.Resource}");
        }

        public int PendingTransactionCount => _transactions.Count;
    }
}
