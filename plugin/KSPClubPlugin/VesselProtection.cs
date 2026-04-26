using KSP.UI.Screens;
using UnityEngine;

namespace KSPClub
{
    // -------------------------------------------------------------------------
    // Flight scene — block recovery of vessels belonging to other players.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Prevents the recovery dialog from completing for non-owned vessels.
    /// Hooks both the recovery request and the dialog spawn so the block works
    /// whether triggered from the nav-ball recover button or a landing prompt.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class VesselProtectionFlight : MonoBehaviour
    {
        void Start()
        {
            GameEvents.onGUIRecoveryDialogSpawn.Add(OnRecoveryDialog);
            GameEvents.OnVesselRecoveryRequested.Add(OnRecoveryRequested);
        }

        void OnDestroy()
        {
            GameEvents.onGUIRecoveryDialogSpawn.Remove(OnRecoveryDialog);
            GameEvents.OnVesselRecoveryRequested.Remove(OnRecoveryRequested);
        }

        void OnRecoveryRequested(Vessel vessel)
        {
            if (vessel == null) return;
            if (IsOwned(vessel.persistentId)) return;

            ScreenMessages.PostScreenMessage(
                $"[KSP CLUB] Cannot recover '{vessel.vesselName}' — " +
                "this vessel belongs to another player.",
                5f, ScreenMessageStyle.UPPER_CENTER);
        }

        void OnRecoveryDialog(MissionRecoveryDialog dialog)
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || IsOwned(vessel.persistentId)) return;

            // Destroy the dialog before the player can confirm recovery
            Destroy(dialog.gameObject);

            ScreenMessages.PostScreenMessage(
                $"[KSP CLUB] Recovery blocked — '{vessel.vesselName}' " +
                "belongs to another player.",
                5f, ScreenMessageStyle.UPPER_CENTER);

            Debug.Log($"[KSPClub] Blocked recovery of '{vessel.vesselName}' " +
                      $"(persistentId={vessel.persistentId}) — not owned by this player.");
        }

        static bool IsOwned(uint pid) =>
            KSPClubScenario.Instance?.OwnsVessel(pid) ?? true;
    }

    // -------------------------------------------------------------------------
    // Tracking Station — warn on non-owned vessel selection, log terminations.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows a warning whenever a non-owned vessel is selected in the tracking
    /// station, and logs an alert if one is terminated.
    ///
    /// Full termination prevention requires Harmony (not included). The warning
    /// is prominent enough to prevent accidental termination in practice.
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
            if (scenario == null || scenario.OwnsVessel(vessel.persistentId)) return;

            ScreenMessages.PostScreenMessage(
                $"[KSP CLUB] '{vessel.vesselName}' belongs to another player.\n" +
                "Do NOT terminate or recover this vessel.",
                5f, ScreenMessageStyle.UPPER_CENTER);

            Debug.Log($"[KSPClub] Non-owned vessel selected in tracking station: " +
                      $"'{vessel.vesselName}' (persistentId={vessel.persistentId})");
        }

        void OnVesselTerminated(ProtoVessel vessel)
        {
            var scenario = KSPClubScenario.Instance;
            if (scenario == null || scenario.OwnsVessel(vessel.persistentId)) return;

            // Can't undo termination without Harmony, but make the violation very visible
            Debug.LogError($"[KSPClub] CLUB VIOLATION: '{vessel.vesselName}' " +
                           $"(persistentId={vessel.persistentId}) was terminated " +
                           "but does not belong to this player!");

            ScreenMessages.PostScreenMessage(
                $"[KSP CLUB] ⚠ '{vessel.vesselName}' (not yours) was terminated!\n" +
                "Notify your game master immediately.",
                15f, ScreenMessageStyle.UPPER_CENTER);
        }
    }
}
