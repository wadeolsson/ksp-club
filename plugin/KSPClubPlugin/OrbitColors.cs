using System;
using System.Collections.Generic;
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

    // ---- Tracking Station: apply on enter and when selection changes ----

    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public class OrbitColorsTracking : OrbitColorsBase
    {
        void Start() => ApplyColors();
    }

    // ---- Shared implementation ----

    public abstract class OrbitColorsBase : MonoBehaviour
    {
        // Populated from save data at load time: persistentId -> Color
        // Set by PlayerConfig.ClaimExistingFromNode when it reads vessel nodes.
        public static readonly Dictionary<uint, Color> VesselColorCache
            = new Dictionary<uint, Color>();

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

        static Color GetVesselColor(Vessel vessel)
        {
            var scenario = KSPClubScenario.Instance;
            var cfg      = PlayerConfig.Instance;

            // Active vessel in flight is always ours
            if (vessel.isActiveVessel && cfg != null)
                return cfg.PlayerColor;

            // Our own vessel (by ownership list) — use configured color
            if (scenario != null && cfg != null &&
                scenario.OwnsVessel(vessel.persistentId))
                return cfg.PlayerColor;

            // Another player's vessel — use color cached from save load
            if (VesselColorCache.TryGetValue(vessel.persistentId, out Color cached))
                return cached;

            // Fallback: deterministic hash of persistentId so it's stable for everyone
            return HashColor(vessel.persistentId.ToString());
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
