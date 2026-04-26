using UnityEngine;

namespace KSPClub
{
    /// <summary>
    /// When a player first joins the club their save has no owned Kerbals
    /// (Jeb/Val/Bill/Bob are stripped from merged saves). This addon generates
    /// 4 fresh random Kerbals for them on first entry to the Space Center.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class StarterKerbals : MonoBehaviour
    {
        private const int STARTER_COUNT = 4;

        void Start()
        {
            var scenario = KSPClubScenario.Instance;
            var cfg      = PlayerConfig.Instance;

            if (scenario == null || cfg == null) return;
            if (string.IsNullOrEmpty(cfg.PlayerId)) return;
            if (scenario.OwnedKerbalCount > 0) return;  // already has Kerbals

            AssignStarterKerbals(scenario);
        }

        void AssignStarterKerbals(KSPClubScenario scenario)
        {
            var roster = HighLogic.CurrentGame.CrewRoster;

            int assigned = 0;
            for (int i = 0; i < STARTER_COUNT; i++)
            {
                ProtoCrewMember kerbal = roster.GetNewKerbal(ProtoCrewMember.KerbalType.Crew);
                if (kerbal == null) continue;

                kerbal.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                roster.AddCrewMember(kerbal);
                scenario.ClaimKerbal(kerbal.name);
                assigned++;

                Debug.Log($"[KSPClub] Assigned starter Kerbal: {kerbal.name} ({kerbal.trait})");
            }

            if (assigned > 0)
            {
                // Save so the new roster is persisted immediately
                GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);

                ScreenMessages.PostScreenMessage(
                    $"[KSP CLUB] Welcome! {assigned} Kerbals have been assigned to " +
                    $"{PlayerConfig.Instance.AgencyName}.\n" +
                    "Check the Astronaut Complex to meet your crew.",
                    8f, ScreenMessageStyle.UPPER_CENTER);
            }
        }
    }
}
