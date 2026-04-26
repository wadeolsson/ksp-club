using System.Collections;
using KSP.UI.Screens;
using UnityEngine;

namespace KSPClub
{
    // -------------------------------------------------------------------------
    // Tracking Station — disable Fly/Recover/Delete for non-owned vessels.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Disables the Fly, Recover, and Delete buttons whenever a vessel that
    /// doesn't belong to this player is selected in the tracking station.
    /// KSP naturally restores button states when the player selects a different
    /// vessel, so we only need to override in the non-owned direction.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public class VesselProtectionTracking : MonoBehaviour
    {
        private uint _lastSelectedId;

        void Start()
        {
            GameEvents.onVesselTerminated.Add(OnVesselTerminated);
        }

        void OnDestroy()
        {
            GameEvents.onVesselTerminated.Remove(OnVesselTerminated);
        }

        void Update()
        {
            var station = SpaceTracking.Instance;
            if (station == null) return;

            var vessel   = station.SelectedVessel;
            uint current = vessel?.persistentId ?? 0;

            if (current == _lastSelectedId) return; // selection unchanged
            _lastSelectedId = current;

            if (vessel == null) return;

            var scenario = KSPClubScenario.Instance;
            bool owned   = scenario == null || scenario.OwnsVessel(vessel.persistentId);

            SetActionButtons(station, owned);

            if (!owned)
            {
                string owner = vessel.GetDisplayName(); // future: show actual owner
                ScreenMessages.PostScreenMessage(
                    $"[KSP CLUB] '{vessel.vesselName}' belongs to another player.\n" +
                    "Fly, recover, and delete are disabled.",
                    4f, ScreenMessageStyle.UPPER_CENTER);

                Debug.Log($"[KSPClub] Locked action buttons for non-owned vessel " +
                          $"'{vessel.vesselName}' (id={vessel.persistentId})");
            }
        }

        static void SetActionButtons(SpaceTracking station, bool interactable)
        {
            if (station.FlyButton    != null) station.FlyButton.interactable    = interactable;
            if (station.RecoverButton != null) station.RecoverButton.interactable = interactable;
            if (station.DeleteButton  != null) station.DeleteButton.interactable  = interactable;
        }

        void OnVesselTerminated(ProtoVessel vessel)
        {
            var scenario = KSPClubScenario.Instance;
            if (scenario == null || scenario.OwnsVessel(vessel.persistentId)) return;

            Debug.LogError($"[KSPClub] CLUB VIOLATION: '{vessel.vesselName}' " +
                           $"(id={vessel.persistentId}) was terminated but does not " +
                           "belong to this player!");

            ScreenMessages.PostScreenMessage(
                $"[KSP CLUB] WARNING: '{vessel.vesselName}' (not yours) was terminated!\n" +
                "Notify your game master immediately.",
                15f, ScreenMessageStyle.UPPER_CENTER);
        }
    }

    // -------------------------------------------------------------------------
    // Flight — eject player if they somehow enter flight with a non-owned vessel.
    // -------------------------------------------------------------------------

    /// <summary>
    /// If a player enters flight or switches to a vessel they don't own,
    /// show a message and return them to the tracking station.
    ///
    /// Also blocks the recovery dialog for non-owned vessels.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class VesselProtectionFlight : MonoBehaviour
    {
        void Start()
        {
            GameEvents.onGUIRecoveryDialogSpawn.Add(OnRecoveryDialog);
            GameEvents.OnVesselRecoveryRequested.Add(OnRecoveryRequested);
            GameEvents.onVesselChange.Add(OnVesselChange);

            // Check the vessel we loaded into — delay to let VesselTagger claim first
            StartCoroutine(CheckOnLoad());
        }

        void OnDestroy()
        {
            GameEvents.onGUIRecoveryDialogSpawn.Remove(OnRecoveryDialog);
            GameEvents.OnVesselRecoveryRequested.Remove(OnRecoveryRequested);
            GameEvents.onVesselChange.Remove(OnVesselChange);
        }

        // ------------------------------------------------------------------ vessel control

        IEnumerator CheckOnLoad()
        {
            yield return new WaitForSeconds(1f); // let VesselTagger claim new vessels first
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel != null) CheckControl(vessel);
        }

        void OnVesselChange(Vessel vessel)
        {
            if (vessel != null) StartCoroutine(CheckAfterChange(vessel));
        }

        IEnumerator CheckAfterChange(Vessel vessel)
        {
            yield return new WaitForSeconds(0.5f);
            if (vessel.isActiveVessel) CheckControl(vessel);
        }

        void CheckControl(Vessel vessel)
        {
            if (IsOwned(vessel.persistentId)) return;
            StartCoroutine(EjectToTrackingStation(vessel.vesselName));
        }

        IEnumerator EjectToTrackingStation(string vesselName)
        {
            ScreenMessages.PostScreenMessage(
                $"[KSP CLUB] You cannot fly '{vesselName}' — " +
                "this vessel belongs to another player.",
                3f, ScreenMessageStyle.UPPER_CENTER);

            Debug.Log($"[KSPClub] Ejecting to tracking station — " +
                      $"'{vesselName}' is not owned by this player.");

            yield return new WaitForSeconds(3f);
            HighLogic.LoadScene(GameScenes.TRACKSTATION);
        }

        // ------------------------------------------------------------------ recovery block

        void OnRecoveryRequested(Vessel vessel)
        {
            if (vessel == null || IsOwned(vessel.persistentId)) return;

            ScreenMessages.PostScreenMessage(
                $"[KSP CLUB] Cannot recover '{vessel.vesselName}' — " +
                "this vessel belongs to another player.",
                5f, ScreenMessageStyle.UPPER_CENTER);
        }

        void OnRecoveryDialog(MissionRecoveryDialog dialog)
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || IsOwned(vessel.persistentId)) return;

            Destroy(dialog.gameObject);

            ScreenMessages.PostScreenMessage(
                $"[KSP CLUB] Recovery blocked — '{vessel.vesselName}' " +
                "belongs to another player.",
                5f, ScreenMessageStyle.UPPER_CENTER);
        }

        // ------------------------------------------------------------------ helpers

        static bool IsOwned(uint pid) =>
            KSPClubScenario.Instance?.OwnsVessel(pid) ?? true;
    }
}
