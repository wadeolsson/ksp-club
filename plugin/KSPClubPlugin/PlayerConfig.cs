using System.Collections;
using System.IO;
using UnityEngine;

namespace KSPClub
{
    /// <summary>
    /// Persists across all scene loads (persist=true).
    /// Loads the player ID from disk, shows a setup dialog on first play,
    /// and stamps playerID into VESSEL nodes when the game is saved.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class PlayerConfig : MonoBehaviour
    {
        public static PlayerConfig Instance { get; private set; } = null!;

        public string PlayerId { get; private set; } = "";

        private static string ConfigPath =>
            Path.Combine(KSPUtil.ApplicationRootPath,
                "GameData/KSPClubPlugin/PluginData/player.cfg");

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Load();
            GameEvents.onLevelWasLoaded.Add(OnSceneLoaded);
            GameEvents.onGameStateSave.Add(OnGameStateSave);
        }

        void OnDestroy()
        {
            GameEvents.onLevelWasLoaded.Remove(OnSceneLoaded);
            GameEvents.onGameStateSave.Remove(OnGameStateSave);
        }

        // ------------------------------------------------------------------ scene hook

        void OnSceneLoaded(GameScenes scene)
        {
            // Show setup dialog the first time the player enters an active scene
            if (string.IsNullOrEmpty(PlayerId) &&
                (scene == GameScenes.SPACECENTER || scene == GameScenes.FLIGHT))
            {
                StartCoroutine(ShowDialogNextFrame());
            }
        }

        IEnumerator ShowDialogNextFrame()
        {
            yield return null;
            ShowSetupDialog();
        }

        // ------------------------------------------------------------------ save hook

        void OnGameStateSave(ConfigNode gameNode)
        {
            if (string.IsNullOrEmpty(PlayerId)) return;

            var scenario = KSPClubScenario.Instance;
            if (scenario == null) return;

            var flightState = gameNode.GetNode("FLIGHTSTATE");
            if (flightState == null) return;

            int tagged = 0;
            foreach (ConfigNode vesselNode in flightState.GetNodes("VESSEL"))
            {
                if (uint.TryParse(vesselNode.GetValue("persistentId"), out uint pid) &&
                    scenario.OwnsVessel(pid))
                {
                    vesselNode.RemoveValue("playerID");
                    vesselNode.AddValue("playerID", PlayerId);
                    tagged++;
                }
            }

            if (tagged > 0)
                Debug.Log($"[KSPClub] Tagged {tagged} vessel(s) with playerID={PlayerId}");
        }

        // ------------------------------------------------------------------ dialog

        public void ShowSetupDialog()
        {
            string inputId = PlayerId;

            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    "KSPClubSetup",
                    string.IsNullOrEmpty(PlayerId)
                        ? "Welcome to KSP CLUB!\n\nEnter your player ID to tag your vessels.\n" +
                          "This must match the ID your game master registered for you (e.g. <b>wade</b>)."
                        : $"Current player ID: <b>{PlayerId}</b>\n\nEnter a new ID to change it.",
                    "KSP CLUB — Player Setup",
                    HighLogic.UISkin,
                    320f,
                    new DialogGUITextInput(
                        inputId, "player id", false, 24,
                        s => { inputId = s; return s; }, 28f),
                    new DialogGUIButton("Save", () => SetPlayerId(inputId)),
                    new DialogGUIButton("Cancel", null, false)
                ),
                false,
                HighLogic.UISkin
            );
        }

        // ------------------------------------------------------------------ id management

        public void SetPlayerId(string id)
        {
            PlayerId = id.Trim().ToLower();
            Save();
            ScreenMessages.PostScreenMessage(
                $"[KSP CLUB] Player ID set to '{PlayerId}'",
                4f, ScreenMessageStyle.UPPER_CENTER);
            Debug.Log($"[KSPClub] Player ID saved: {PlayerId}");
        }

        // ------------------------------------------------------------------ persistence

        void Load()
        {
            if (!File.Exists(ConfigPath)) return;
            ConfigNode node = ConfigNode.Load(ConfigPath);
            PlayerId = node?.GetValue("playerId") ?? "";
            if (!string.IsNullOrEmpty(PlayerId))
                Debug.Log($"[KSPClub] Loaded player ID: {PlayerId}");
        }

        void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            ConfigNode node = new ConfigNode("KSPCLUB_PLAYER");
            node.AddValue("playerId", PlayerId);
            node.Save(ConfigPath);
        }
    }
}
