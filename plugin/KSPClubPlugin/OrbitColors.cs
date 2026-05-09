using System;
using System.Collections.Generic;
using KSP.UI.Screens;
using UnityEngine;

namespace KSPClub
{
    /// <summary>
    /// Colors each vessel's orbit line by its owning player.
    /// Each player configures their own color; other players' vessels
    /// fall back to a deterministic hash-based color derived from their playerID.
    ///
    /// Colors are applied when the map view opens (flight) or on scene enter
    /// (tracking station), and refreshed whenever vessels are added/removed.
    /// </summary>

    // ---- Flight: apply when map view is opened ----

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class OrbitColorsFlight : OrbitColorsBase
    {
        private bool _mapWasOpen;

        void Update()
        {
            bool mapOpen = MapView.MapIsEnabled;
            if (mapOpen && !_mapWasOpen) ApplyColors();
            _mapWasOpen = mapOpen;
        }
    }

    // ---- Tracking Station: apply on enter, on selection change, and periodically ----

    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public class OrbitColorsTracking : OrbitColorsBase
    {
        private float _nextApply;
        private uint  _lastSelectedId;

        void Start()
        {
            ApplyColors();
            _nextApply = Time.time + 2f;
        }

        void Update()
        {
            // Reapply periodically — KSP resets colors on vessel list interaction
            if (Time.time >= _nextApply)
            {
                ApplyColors();
                _nextApply = Time.time + 3f;
            }

            // Also reapply immediately when the selected vessel changes
            uint cur = SpaceTracking.Instance?.SelectedVessel?.persistentId ?? 0;
            if (cur != _lastSelectedId)
            {
                _lastSelectedId = cur;
                ApplyColors();
            }
        }
    }

    // ---- Shared implementation ----

    public abstract class OrbitColorsBase : MonoBehaviour
    {
        // Populated from save data at load time: persistentId -> Color
        public static readonly Dictionary<uint, Color> VesselColorCache
            = new Dictionary<uint, Color>();

        // Populated from save data at load time: persistentId -> playerID string
        public static readonly Dictionary<uint, string> VesselOwnerCache
            = new Dictionary<uint, string>();

        // Named preset colors players can choose from
        public static readonly Dictionary<string, Color> Presets = new Dictionary<string, Color>
        {
            ["blue"]   = new Color(0.20f, 0.55f, 1.00f),
            ["red"]    = new Color(1.00f, 0.25f, 0.25f),
            ["green"]  = new Color(0.20f, 0.90f, 0.35f),
            ["orange"] = new Color(1.00f, 0.60f, 0.10f),
            ["purple"] = new Color(0.75f, 0.20f, 1.00f),
            ["yellow"] = new Color(1.00f, 0.90f, 0.15f),
            ["cyan"]   = new Color(0.10f, 0.90f, 0.90f),
            ["pink"]   = new Color(1.00f, 0.35f, 0.70f),
            ["white"]  = Color.white,
        };

        protected void ApplyColors()
        {
            var vessels = FlightGlobals.Vessels;
            if (vessels == null) return;

            foreach (var vessel in vessels)
            {
                if (vessel?.orbitDriver?.Renderer == null) continue;
                var color = GetVesselColor(vessel);
                vessel.orbitDriver.Renderer.orbitColor = color;
                vessel.orbitDriver.Renderer.nodeColor  = color;
            }
        }

        // Gold color used for active tanker vessels — visible from any relation stance
        public static readonly Color TankerColor = new Color(1.0f, 0.80f, 0.05f, 1f);

        static Color GetVesselColor(Vessel vessel)
        {
            var scenario = KSPClubScenario.Instance;
            var cfg      = PlayerConfig.Instance;

            // Tanker vessels always show gold regardless of owner — instantly identifiable
            bool isTanker = FuelTanker.TankerCache.TryGetValue(vessel.persistentId, out var tc)
                            && tc.Active;
            if (!isTanker && scenario != null && scenario.IsTanker(vessel.persistentId))
                isTanker = true;
            if (isTanker)
                return TankerColor;

            // Active vessel in flight is always ours
            if (vessel.isActiveVessel && cfg != null)
                return cfg.PlayerColor;

            // Our own vessel (by ownership list) — use configured color
            if (scenario != null && cfg != null &&
                scenario.OwnsVessel(vessel.persistentId))
                return cfg.PlayerColor;

            // Determine relation to this vessel's owner
            VesselOwnerCache.TryGetValue(vessel.persistentId, out string ownerId);
            Relation relation = cfg?.GetRelation(ownerId ?? "") ?? Relation.Neutral;

            // Get the base color for this vessel
            Color baseColor = VesselColorCache.TryGetValue(vessel.persistentId, out Color cached)
                ? cached
                : HashColor(vessel.persistentId.ToString());

            // Apply relation-based modulation
            return relation switch
            {
                Relation.Friendly => baseColor,
                Relation.Neutral  => Color.Lerp(baseColor, Color.gray, 0.35f),
                Relation.Hostile  => new Color(0.55f, 0.05f, 0.05f, 0.4f),
                _                 => baseColor,
            };
        }

        public static Color ParseColor(string s)
        {
            // Stored as "R,G,B"
            var parts = s.Split(',');
            if (parts.Length >= 3 &&
                float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float r) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float g) &&
                float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float b))
                return new Color(r, g, b);
            return Color.clear;
        }

        public static string ColorToString(Color c) =>
            $"{c.r.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
            $"{c.g.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
            $"{c.b.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        static Color HashColor(string playerID)
        {
            if (string.IsNullOrEmpty(playerID)) return Color.white;
            float hue = (Math.Abs(playerID.GetHashCode()) % 100) / 100f;
            return Color.HSVToRGB(hue, 0.85f, 1.0f);
        }
    }
}
