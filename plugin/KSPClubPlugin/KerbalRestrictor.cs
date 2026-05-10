using System.Collections.Generic;
using UnityEngine;

namespace KSPClub
{
    /// <summary>
    /// Protects Kerbal ownership:
    ///   - Stock Kerbals (Jeb, Val, Bill, Bob): warns, should not be used
    ///   - Other players' custom Kerbals: blocks assignment and warns
    ///
    /// Checks happen at:
    ///   - Astronaut Complex open (reminder banner)
    ///   - Vessel launch / creation (crew scan)
    ///   - Crew board / EVA events
    /// </summary>
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class KerbalRestrictor : MonoBehaviour
    {
        public static bool IsStockKerbal(string name) => StockKerbals.Contains(name);

        private static readonly HashSet<string> StockKerbals = new HashSet<string>
        {
            "Jebediah Kerman", "Valentina Kerman", "Bill Kerman", "Bob Kerman",
            "Lodwin Kerman",   "Genelan Kerman",   "Aldoly Kerman", "Shepoly Kerman",
            "Mortimer Kerman", "Wernher von Kerman",
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
                "[KSP CLUB] Hire random recruits only — do not use Jeb, Val, Bill, or Bob,\n" +
                "and do not assign Kerbals that belong to other players.",
                7f, ScreenMessageStyle.UPPER_CENTER);
        }

        // ------------------------------------------------------------------ public helpers

        /// <summary>True if this Kerbal belongs to another player (not us, not stock).</summary>
        public static bool IsOtherPlayersKerbal(string name)
        {
            if (IsStockKerbal(name)) return false;
            var scenario = KSPClubScenario.Instance;
            if (scenario == null) return false;
            return !scenario.OwnsKerbal(name);
        }

        /// <summary>
        /// Scan a vessel's crew. Warn loudly for stock and other-player Kerbals.
        /// Returns true if any violations found.
        /// </summary>
        public static bool CheckVesselCrew(Vessel vessel)
        {
            if (vessel?.GetVesselCrew() == null) return false;
            bool bad = false;
            foreach (var kerbal in vessel.GetVesselCrew())
            {
                if (IsStockKerbal(kerbal.name))
                {
                    ScreenMessages.PostScreenMessage(
                        $"[KSP CLUB] '{kerbal.name}' is a stock Kerbal and should not fly club missions.",
                        8f, ScreenMessageStyle.UPPER_CENTER);
                    bad = true;
                }
                else if (IsOtherPlayersKerbal(kerbal.name))
                {
                    ScreenMessages.PostScreenMessage(
                        $"[KSP CLUB] '{kerbal.name}' belongs to another player!\n" +
                        "Remove them from this vessel before launching.",
                        10f, ScreenMessageStyle.UPPER_CENTER);
                    bad = true;
                }
            }
            return bad;
        }
    }

    /// <summary>
    /// Flight-scene Kerbal protection. Warns at vessel creation and on crew board events.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KerbalRestrictorFlight : MonoBehaviour
    {
        void Start()
        {
            GameEvents.onVesselCreate.Add(OnVesselCreate);
            GameEvents.onCrewBoardVessel.Add(OnCrewBoard);
            GameEvents.onCrewTransferred.Add(OnCrewTransferred);
        }

        void OnDestroy()
        {
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
            GameEvents.onCrewBoardVessel.Remove(OnCrewBoard);
            GameEvents.onCrewTransferred.Remove(OnCrewTransferred);
        }

        void OnVesselCreate(Vessel vessel)
        {
            // Short delay so crew roster is fully populated before we check
            StartCoroutine(CheckCrewNextFrame(vessel));
        }

        System.Collections.IEnumerator CheckCrewNextFrame(Vessel vessel)
        {
            yield return null;
            KerbalRestrictor.CheckVesselCrew(vessel);
        }

        void OnCrewBoard(GameEvents.FromToAction<Part, Part> data)
        {
            // A Kerbal just boarded — check who it is via the part's crew
            if (data.to?.protoModuleCrew == null) return;
            foreach (var kerbal in data.to.protoModuleCrew)
            {
                if (KerbalRestrictor.IsOtherPlayersKerbal(kerbal.name))
                {
                    ScreenMessages.PostScreenMessage(
                        $"[KSP CLUB] '{kerbal.name}' belongs to another player — " +
                        "do not crew their Kerbals on your vessels.",
                        8f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
        }

        void OnCrewTransferred(GameEvents.HostedFromToAction<ProtoCrewMember, Part> data)
        {
            if (data.host == null) return;
            if (KerbalRestrictor.IsOtherPlayersKerbal(data.host.name))
            {
                ScreenMessages.PostScreenMessage(
                    $"[KSP CLUB] '{data.host.name}' belongs to another player.\n" +
                    "Crew transfers of other players' Kerbals are not allowed.",
                    8f, ScreenMessageStyle.UPPER_CENTER);
            }
        }
    }
}
