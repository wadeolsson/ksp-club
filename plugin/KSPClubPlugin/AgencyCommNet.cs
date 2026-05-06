using System.Collections.Generic;
using UnityEngine;

namespace KSPClub
{
    /// <summary>
    /// Agency-locked CommNet: relay probes only forward signals for vessels
    /// belonging to friendly agencies.
    ///
    /// Every UPDATE_INTERVAL seconds, iterates all vessels and:
    ///   - Friendly agency vessels: relay power left at full (original value)
    ///   - Neutral agency vessels:  relay power set to 0 (no relay, direct only)
    ///   - Hostile agency vessels:  relay power set to 0
    ///
    /// Own vessels are never affected. Original relay powers are cached so they
    /// can be restored if the player changes a relation.
    ///
    /// Because each player plays in their own save, this affects only the local
    /// player's CommNet — exactly the per-player isolation we want.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class AgencyCommNet : MonoBehaviour
    {
        private const float UPDATE_INTERVAL = 6f;  // seconds between network passes

        // Cache of original relay powers so we can restore them
        private readonly Dictionary<uint, double> _originalPowers
            = new Dictionary<uint, double>();

        private float _nextUpdate;

        void Start()
        {
            _nextUpdate = Time.time + 2f; // short delay for CommNet to initialise
        }

        void Update()
        {
            if (Time.time < _nextUpdate) return;
            _nextUpdate = Time.time + UPDATE_INTERVAL;
            ApplyRelayFilter();
        }

        // ------------------------------------------------------------------ filter

        void ApplyRelayFilter()
        {
            var cfg      = PlayerConfig.Instance;
            var scenario = KSPClubScenario.Instance;
            if (cfg == null || scenario == null) return;

            foreach (var vessel in FlightGlobals.Vessels)
            {
                if (vessel?.connection?.Comm == null) continue;

                var comm = vessel.connection.Comm;

                // Never touch our own vessels
                if (scenario.OwnsVessel(vessel.persistentId)) continue;

                // Cache original relay power the first time we see this vessel
                if (!_originalPowers.ContainsKey(vessel.persistentId))
                    _originalPowers[vessel.persistentId] = comm.antennaRelay.power;

                // Restore original first, then decide
                comm.antennaRelay.power = _originalPowers[vessel.persistentId];

                // Determine relation to this vessel's owner
                OrbitColorsBase.VesselOwnerCache.TryGetValue(
                    vessel.persistentId, out string ownerId);
                Relation relation = cfg.GetRelation(ownerId ?? "");

                if (relation != Relation.Friendly)
                {
                    // Zero out relay — vessel can still receive/transmit directly
                    // but won't act as a relay hop for us
                    comm.antennaRelay.power = 0;
                }
            }
        }
    }
}
