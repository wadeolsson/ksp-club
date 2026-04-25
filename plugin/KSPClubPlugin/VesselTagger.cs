using UnityEngine;

namespace KSPClub
{
    /// <summary>
    /// Active in Flight scene. Claims any vessel that is created while the
    /// player is playing — including newly launched rockets and separated debris.
    ///
    /// The actual playerID stamp happens in PlayerConfig.OnGameStateSave so it
    /// is written regardless of which scene the player is in when they save.
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

            // Warn if no player ID is set yet — vessel will remain untagged
            if (string.IsNullOrEmpty(PlayerConfig.Instance?.PlayerId))
            {
                ScreenMessages.PostScreenMessage(
                    "[KSP CLUB] Warning: player ID not set. This vessel will not be tagged.\n" +
                    "Open the KSP CLUB setup dialog to fix this.",
                    6f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            KSPClubScenario.Instance?.ClaimVessel(vessel.persistentId);

            Debug.Log($"[KSPClub] New vessel '{vessel.vesselName}' " +
                      $"(persistentId={vessel.persistentId}) claimed for " +
                      $"player '{PlayerConfig.Instance!.PlayerId}'");
        }
    }
}
