using System.Collections.Generic;
using KSP.UI.Screens;
using UnityEngine;

namespace KSPClub
{
    /// <summary>
    /// Vessel transfer system. Allows a player to formally gift one of their
    /// vessels to another club member.
    ///
    /// How it works:
    ///   1. Player opens the Transfer dialog, picks a vessel and enters the
    ///      target player's ID.
    ///   2. The plugin stamps transferTarget = targetId on the vessel and
    ///      removes it from OWNED_VESSELS so it's no longer treated as ours.
    ///   3. On the next save the vessel is written to disk with the transfer stamp.
    ///   4. On the next merge the merger reads transferTarget, reassigns playerID
    ///      to the target, and routes the vessel into the target's save.
    ///   5. The target's plugin claims it on their next load.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public class VesselTrading : MonoBehaviour
    {
        private ApplicationLauncherButton? _button;

        // Pending transfers: persistentId → targetPlayerId
        // Set when player confirms a transfer; applied in PlayerConfig.OnProtoVesselSave
        public static readonly Dictionary<uint, string> PendingTransfers
            = new Dictionary<uint, string>();

        // ------------------------------------------------------------------ toolbar

        void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
        }

        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            if (_button != null)
                ApplicationLauncher.Instance.RemoveModApplication(_button);
        }

        void AddButton()
        {
            if (_button != null) return;
            _button = ApplicationLauncher.Instance.AddModApplication(
                OnButtonClick, OnButtonClick,
                null, null, null, null,
                ApplicationLauncher.AppScenes.TRACKSTATION,
                MakeIcon()
            );
        }

        void OnButtonClick()
        {
            if (_button != null) _button.SetFalse(false);
            ShowTransferDialog();
        }

        // ------------------------------------------------------------------ transfer dialog

        void ShowTransferDialog()
        {
            var cfg      = PlayerConfig.Instance;
            var scenario = KSPClubScenario.Instance;
            if (cfg == null || scenario == null)
            {
                PlayerConfig.ShowError("Not in a club save.");
                return;
            }

            // Build list of owned vessels currently in the game
            var ownedVessels = new List<Vessel>();
            foreach (var vessel in FlightGlobals.Vessels)
                if (scenario.OwnsVessel(vessel.persistentId))
                    ownedVessels.Add(vessel);

            if (ownedVessels.Count == 0)
            {
                PlayerConfig.ShowError("You have no vessels to transfer.");
                return;
            }

            // One button per vessel + a target ID input
            string targetId  = "";
            uint   selectedId = 0;
            string selectedName = "";

            var elements = new List<DialogGUIBase>();
            elements.Add(new DialogGUILabel(
                "Select a vessel to transfer to another player.\n" +
                "The transfer takes effect after the next weekly merge."));
            elements.Add(new DialogGUILabel("<b>Your vessels:</b>"));

            foreach (var vessel in ownedVessels)
            {
                uint   vid  = vessel.persistentId;
                string name = vessel.vesselName;
                string sit  = vessel.situation.ToString();
                elements.Add(new DialogGUIButton(
                    $"{name}  ({sit})",
                    () => { selectedId = vid; selectedName = name; },
                    false));
            }

            elements.Add(new DialogGUILabel("<b>Transfer to player ID:</b>"));
            elements.Add(new DialogGUITextInput(targetId, "e.g. kent", false, 32,
                s => { targetId = s; return s; }, 28f));

            elements.Add(new DialogGUIButton("Confirm Transfer", () =>
            {
                if (selectedId == 0)
                {
                    PlayerConfig.ShowError("Select a vessel first.");
                    return;
                }
                string tid = targetId.Trim().ToLower();
                if (string.IsNullOrEmpty(tid) || tid == cfg.PlayerId)
                {
                    PlayerConfig.ShowError("Enter a valid target player ID.");
                    return;
                }
                ConfirmTransfer(selectedId, selectedName, tid);
            }, false));

            elements.Add(new DialogGUIButton("Cancel", null, false));

            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    "KSPClubTransfer", "",
                    "KSP CLUB — Transfer Vessel",
                    HighLogic.UISkin, 400f,
                    elements.ToArray()),
                false, HighLogic.UISkin);
        }

        void ConfirmTransfer(uint persistentId, string vesselName, string targetPlayerId)
        {
            var scenario = KSPClubScenario.Instance;
            if (scenario == null) return;

            // Queue the transfer — applied in onProtoVesselSave
            PendingTransfers[persistentId] = targetPlayerId;

            // Remove from our owned list immediately
            scenario.ReleaseVessel(persistentId);

            // Force a save so the transferTarget stamp hits disk
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);

            Debug.Log($"[KSPClub] Transfer initiated: '{vesselName}' → {targetPlayerId}");

            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    "KSPClubTransferDone",
                    $"Transfer initiated!\n\n" +
                    $"<b>{vesselName}</b> will belong to <b>{targetPlayerId}</b> " +
                    "after the next weekly merge.\n\n" +
                    "Submit your save this week to complete the transfer.",
                    "KSP CLUB — Transfer Queued",
                    HighLogic.UISkin,
                    new DialogGUIButton("OK", null, true)),
                false, HighLogic.UISkin);
        }

        // ------------------------------------------------------------------ icon

        static Texture2D MakeIcon()
        {
            var tex = GameDatabase.Instance?.GetTexture("KSPClubPlugin/icon_transfer", false);
            if (tex != null) return tex;
            // Fallback
            const int size = 38;
            tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var green = new Color(0.20f, 0.75f, 0.30f, 1f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, (x < 2 || x >= size-2 || y < 2 || y >= size-2) ? Color.white : green);
            tex.Apply();
            return tex;
        }
    }
}
