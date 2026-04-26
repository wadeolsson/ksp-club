using System;
using System.Collections;
using System.IO;
using KSP.UI.Screens;
using UnityEngine;

namespace KSPClub
{
    /// <summary>
    /// Space Center toolbar button for manual save submission and sync status.
    /// Adds a button to the stock application launcher toolbar.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class SaveSyncUI : MonoBehaviour
    {
        private ApplicationLauncherButton? _button;
        private bool _submitting;

        // ------------------------------------------------------------------ lifecycle

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

        // ------------------------------------------------------------------ toolbar button

        void AddButton()
        {
            if (_button != null) return;
            _button = ApplicationLauncher.Instance.AddModApplication(
                OnButtonClick, OnButtonClick,
                null, null, null, null,
                ApplicationLauncher.AppScenes.SPACECENTER,
                MakeIcon()
            );
        }

        void OnButtonClick()
        {
            if (_button != null) _button.SetFalse(false);

            if (!PlayerConfig.Instance.SyncConfigured)
            {
                PlayerConfig.Instance.ShowSetupDialog();
                return;
            }

            ShowSyncDialog();
        }

        // ------------------------------------------------------------------ sync dialog

        void ShowSyncDialog()
        {
            var cfg = PlayerConfig.Instance;
            if (cfg == null)
            {
                Debug.LogError("[KSPClub] PlayerConfig.Instance is null in ShowSyncDialog");
                return;
            }

            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    "KSPClubSync",
                    $"<b>KSP CLUB Sync</b>\n\n" +
                    $"Player: <b>{cfg.PlayerId}</b>\n" +
                    $"Save:   <b>{cfg.SaveName}</b>\n" +
                    $"Repo:   {cfg.RepoOwner}/{cfg.RepoName}\n\n" +
                    "Submit your save to the club for this week's merge.\n" +
                    "The game will be saved first.",
                    "KSP CLUB — Save Sync",
                    HighLogic.UISkin,
                    380f,
                    new DialogGUIButton("Submit My Save", OnSubmitClicked),
                    new DialogGUIButton("Settings", () => PlayerConfig.Instance?.ShowSetupDialog(), true),
                    new DialogGUIButton("Close", null, false)
                ),
                false,
                HighLogic.UISkin
            );
        }

        void OnSubmitClicked()
        {
            if (_submitting) return;
            StartCoroutine(SubmitSave());
        }

        // ------------------------------------------------------------------ submission pipeline

        IEnumerator SubmitSave()
        {
            _submitting = true;
            var cfg = PlayerConfig.Instance;

            // Step 1 — trigger a KSP save so the file on disk is current
            ScreenMessages.PostScreenMessage(
                "[KSP CLUB] Saving game...", 5f, ScreenMessageStyle.UPPER_CENTER);

            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);

            yield return null; // wait one frame for write to complete

            // Construct the full path ourselves — SaveGame return value is just the filename
            string savedPath = Path.Combine(
                KSPUtil.ApplicationRootPath, "saves",
                HighLogic.SaveFolder, "persistent.sfs");

            if (!File.Exists(savedPath))
            {
                ShowResult(false, $"Could not find save file at:\n{savedPath}\n\nTry saving manually first.");
                _submitting = false;
                yield break;
            }

            // Step 2 — read the save file from disk
            byte[]? saveData = null;
            try   { saveData = File.ReadAllBytes(savedPath); }
            catch (Exception ex)
            {
                ShowResult(false, $"Could not read save file:\n{ex.Message}");
                _submitting = false;
                yield break;
            }

            ScreenMessages.PostScreenMessage(
                "[KSP CLUB] Uploading save...", 60f, ScreenMessageStyle.UPPER_CENTER);

            var client      = cfg.MakeClient();
            string repoPath = $"submissions/{cfg.PlayerId}/persistent.sfs";

            // Step 3 — get current SHA (needed to update an existing file)
            string? currentSha = null;
            yield return client.GetSha(repoPath, sha => currentSha = sha);

            // Step 4 — upload
            bool ok = false;
            string dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
            yield return client.PutFile(
                repoPath, saveData,
                $"submission: {cfg.PlayerId} {dateStr}",
                currentSha,
                result => ok = result);

            ScreenMessages.PostScreenMessage("", 0f, ScreenMessageStyle.UPPER_CENTER);

            ShowResult(ok,
                ok  ? $"Save submitted successfully!\nYour game master can now run the merge." :
                      "Upload failed. Check your GitHub token and internet connection.");

            _submitting = false;
        }

        // ------------------------------------------------------------------ helpers

        private static void ShowResult(bool success, string message)
        {
            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    "KSPClubResult",
                    (success ? "✓ " : "✗ ") + message,
                    success ? "KSP CLUB — Submitted!" : "KSP CLUB — Error",
                    HighLogic.UISkin,
                    new DialogGUIButton("OK", null, true)),
                false, HighLogic.UISkin);
        }

        /// <summary>Generate a simple coloured square icon for the toolbar.</summary>
        private static Texture2D MakeIcon()
        {
            const int size = 38;
            var tex  = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var blue = new Color(0.18f, 0.52f, 0.90f, 1f);
            var white = Color.white;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Simple "KC" badge: blue background, white border ring
                bool border = x < 2 || x >= size - 2 || y < 2 || y >= size - 2;
                tex.SetPixel(x, y, border ? white : blue);
            }

            tex.Apply();
            return tex;
        }
    }
}
