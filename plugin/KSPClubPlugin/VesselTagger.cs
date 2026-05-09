using System.Collections;
using UnityEngine;

namespace KSPClub
{
    /// <summary>
    /// Active in Flight scene. Claims any vessel created while the player is
    /// playing — including newly launched rockets and separated debris.
    ///
    /// If KSPClubScenario hasn't finished initialising when onVesselCreate
    /// fires (race condition on fresh VAB launches), the claim is retried
    /// every 100 ms for up to 5 seconds.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class VesselTagger : MonoBehaviour
    {
        void Start()
        {
            GameEvents.onVesselCreate.Add(OnVesselCreate);
        }

        void OnDestroy()
        {
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
        }

        void OnVesselCreate(Vessel vessel)
        {
            if (vessel == null) return;

            if (string.IsNullOrEmpty(PlayerConfig.Instance?.PlayerId))
            {
                ScreenMessages.PostScreenMessage(
                    "[KSP CLUB] Warning: player ID not set. This vessel will not be tagged.\n" +
                    "Open the KSP CLUB setup dialog to fix this.",
                    6f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            if (KSPClubScenario.Instance != null)
            {
                KSPClubScenario.Instance.ClaimVessel(vessel.persistentId);
                Debug.Log($"[KSPClub] Claimed '{vessel.vesselName}' (pid={vessel.persistentId})");
            }
            else
            {
                // Scenario not ready yet — retry until it is
                StartCoroutine(ClaimWhenReady(vessel.persistentId, vessel.vesselName));
            }
        }

        IEnumerator ClaimWhenReady(uint pid, string vesselName)
        {
            float waited = 0f;
            while (KSPClubScenario.Instance == null && waited < 5f)
            {
                yield return new WaitForSeconds(0.1f);
                waited += 0.1f;
            }

            if (KSPClubScenario.Instance != null)
            {
                KSPClubScenario.Instance.ClaimVessel(pid);
                Debug.Log($"[KSPClub] Claimed '{vesselName}' after {waited:F1}s delay");
            }
            else
            {
                Debug.LogWarning($"[KSPClub] Could not claim '{vesselName}' — scenario never initialised");
            }
        }
    }
}
