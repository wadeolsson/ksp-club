using System.Collections.Generic;
using UnityEngine;

namespace KSPClub
{
    /// <summary>
    /// Warns players against using the stock named Kerbals (Jeb, Val, Bill, Bob).
    /// These are reserved for the shared universe and should never be assigned
    /// to player vessels — they belong to the dynamic layer.
    ///
    /// V1: shows screen messages as reminders.
    /// Future: actively hides stock Kerbals from the Astronaut Complex hire list.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class KerbalRestrictor : MonoBehaviour
    {
        private static readonly HashSet<string> StockKerbals = new HashSet<string>
        {
            "Jebediah Kerman",
            "Valentina Kerman",
            "Bill Kerman",
            "Bob Kerman",
            "Lodwin Kerman",
            "Genelan Kerman",
            "Aldoly Kerman",
            "Shepoly Kerman",
            "Mortimer Kerman",
            "Wernher von Kerman",
        };

        void Start()
        {
            GameEvents.onGUIAstronautComplexSpawn.Add(OnAstronautComplexOpen);
        }

        void OnDestroy()
        {
            GameEvents.onGUIAstronautComplexSpawn.Remove(OnAstronautComplexOpen);
        }

        void OnAstronautComplexOpen()
        {
            ScreenMessages.PostScreenMessage(
                "[KSP CLUB] Do not hire or use the stock Kerbals (Jeb, Val, Bill, Bob).\n" +
                "Recruit random Kerbals only — stock Kerbals belong to the shared universe.",
                8f, ScreenMessageStyle.UPPER_CENTER);
        }

        /// <summary>
        /// Check if a vessel's crew contains any stock Kerbals and warn if so.
        /// Call this on vessel launch from flight scene if needed.
        /// </summary>
        public static bool HasStockCrew(Vessel vessel)
        {
            if (vessel?.GetVesselCrew() == null) return false;
            foreach (var kerbal in vessel.GetVesselCrew())
            {
                if (StockKerbals.Contains(kerbal.name))
                {
                    ScreenMessages.PostScreenMessage(
                        $"[KSP CLUB] Warning: '{kerbal.name}' is a stock Kerbal and should not be used in club missions.",
                        8f, ScreenMessageStyle.UPPER_CENTER);
                    return true;
                }
            }
            return false;
        }
    }
}
