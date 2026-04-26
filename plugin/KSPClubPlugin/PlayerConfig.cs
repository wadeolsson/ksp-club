using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace KSPClub
{
    /// <summary>
    /// Persists across all scene loads (persist=true). Responsibilities:
    ///   - Load/save player config (ID, GitHub token, repo settings)
    ///   - Show first-run setup dialog
    ///   - Stamp playerID into VESSEL nodes on every game save
    ///   - Check for a new merged save on main menu load and offer to download it
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class PlayerConfig : MonoBehaviour
    {
        public static PlayerConfig Instance { get; private set; } = null!;

        // player identity
        public string PlayerId    { get; private set; } = "";

        // GitHub sync settings
        public string GitHubToken { get; private set; } = "";
        public string RepoOwner   { get; private set; } = "wadeolsson";
        public string RepoName    { get; private set; } = "ksp-club-saves";
        public string SaveName    { get; private set; } = "KSP_CLUB";

        // last known SHA of output/<playerId>/persistent.sfs
        private string _lastOutputSha = "";
        private bool   _checkedThisSession;

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

        // ------------------------------------------------------------------ scene hooks

        void OnSceneLoaded(GameScenes scene)
        {
            if (scene == GameScenes.MAINMENU)
            {
                // Show setup dialog if config is incomplete
                if (string.IsNullOrEmpty(PlayerId) || string.IsNullOrEmpty(GitHubToken))
                    StartCoroutine(DelayThen(2f, ShowSetupDialog));

                // Once per session: check if the game master has pushed a new save
                else if (!_checkedThisSession && SyncConfigured)
                {
                    _checkedThisSession = true;
                    StartCoroutine(DelayThen(2f, CheckForNewSave));
                }
            }

            // Show setup dialog on first entry to an active scene if still not configured
            if (string.IsNullOrEmpty(PlayerId) &&
                (scene == GameScenes.SPACECENTER || scene == GameScenes.FLIGHT))
                StartCoroutine(DelayThen(0f, ShowSetupDialog));
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

        // ------------------------------------------------------------------ new-save check

        public bool SyncConfigured =>
            !string.IsNullOrEmpty(PlayerId) &&
            !string.IsNullOrEmpty(GitHubToken) &&
            !string.IsNullOrEmpty(RepoOwner) &&
            !string.IsNullOrEmpty(RepoName);

        void CheckForNewSave()
        {
            StartCoroutine(CheckForNewSaveCoroutine());
        }

        IEnumerator CheckForNewSaveCoroutine()
        {
            var client = MakeClient();
            string remotePath = $"output/{PlayerId}/persistent.sfs";

            string? remoteSha = null;
            yield return client.GetSha(remotePath, sha => remoteSha = sha);

            if (remoteSha == null)
            {
                Debug.Log("[KSPClub] No output save found on remote (not yet merged).");
                yield break;
            }

            if (remoteSha == _lastOutputSha)
            {
                Debug.Log("[KSPClub] Output save is up to date.");
                yield break;
            }

            // New save available — prompt player
            Debug.Log($"[KSPClub] New output save available (sha={remoteSha}).");
            ShowNewSaveDialog(client, remotePath, remoteSha);
        }

        void ShowNewSaveDialog(GitHubClient client, string remotePath, string remoteSha)
        {
            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    "KSPClubNewSave",
                    $"The game master has merged this week's saves.\n\n" +
                    $"A new <b>{SaveName}</b> save is ready to download.\n\n" +
                    "Download now and load it when you start playing?",
                    "KSP CLUB — New Save Available",
                    HighLogic.UISkin,
                    360f,
                    new DialogGUIButton("Download", () =>
                        StartCoroutine(DownloadNewSave(client, remotePath, remoteSha))),
                    new DialogGUIButton("Later", null, false)
                ),
                false,
                HighLogic.UISkin
            );
        }

        IEnumerator DownloadNewSave(GitHubClient client, string remotePath, string newSha)
        {
            ScreenMessages.PostScreenMessage(
                "[KSP CLUB] Downloading new save...",
                60f, ScreenMessageStyle.UPPER_CENTER);

            byte[]? data = null;
            yield return client.DownloadFile(remotePath, bytes => data = bytes);

            ScreenMessages.PostScreenMessage("", 0f, ScreenMessageStyle.UPPER_CENTER);

            if (data == null)
            {
                ShowError("Download failed. Check your internet connection and try again from the Space Center.");
                yield break;
            }

            string savePath = Path.Combine(
                KSPUtil.ApplicationRootPath, "saves", SaveName, "persistent.sfs");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                File.WriteAllBytes(savePath, data);
            }
            catch (Exception ex)
            {
                ShowError($"Could not write save file:\n{ex.Message}");
                yield break;
            }

            _lastOutputSha = newSha;
            Save();
            Debug.Log($"[KSPClub] New save written to {savePath}");

            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    "KSPClubDownloadDone",
                    $"Save downloaded successfully!\n\n" +
                    $"Load the <b>{SaveName}</b> save to play with this week's universe.",
                    "KSP CLUB — Download Complete",
                    HighLogic.UISkin,
                    new DialogGUIButton("OK", null, true)
                ),
                false,
                HighLogic.UISkin
            );
        }

        // ------------------------------------------------------------------ setup dialog

        public void ShowSetupDialog()
        {
            string inputId    = PlayerId;
            string inputToken = GitHubToken;
            string inputOwner = RepoOwner;
            string inputRepo  = RepoName;
            string inputSave  = SaveName;

            bool isFirstRun = string.IsNullOrEmpty(PlayerId);

            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    "KSPClubSetup",
                    isFirstRun
                        ? "Welcome to KSP CLUB!\n\nConfigure your player ID and GitHub token " +
                          "to enable automatic save sync."
                        : "Update your KSP CLUB settings.",
                    "KSP CLUB — Setup",
                    HighLogic.UISkin,
                    380f,
                    new DialogGUILabel("<b>Player ID</b> (assigned by your game master)"),
                    new DialogGUITextInput(inputId, "e.g. wade", false, 32,
                        s => { inputId = s; return s; }, 28f),
                    new DialogGUILabel("<b>GitHub Token</b> (Personal Access Token with repo scope)"),
                    new DialogGUITextInput(inputToken, "ghp_...", true, 100,
                        s => { inputToken = s; return s; }, 28f),
                    new DialogGUILabel("<b>Repo Owner</b>"),
                    new DialogGUITextInput(inputOwner, "wadeolsson", false, 64,
                        s => { inputOwner = s; return s; }, 28f),
                    new DialogGUILabel("<b>Repo Name</b>"),
                    new DialogGUITextInput(inputRepo, "ksp-club-saves", false, 64,
                        s => { inputRepo = s; return s; }, 28f),
                    new DialogGUILabel("<b>Club Save Name</b> (your KSP save folder)"),
                    new DialogGUITextInput(inputSave, "KSP_CLUB", false, 32,
                        s => { inputSave = s; return s; }, 28f),
                    new DialogGUIButton("Save", () =>
                    {
                        SetConfig(inputId, inputToken, inputOwner, inputRepo, inputSave);
                    }),
                    new DialogGUIButton("Cancel", null, false)
                ),
                false,
                HighLogic.UISkin
            );
        }

        // ------------------------------------------------------------------ config management

        public void SetConfig(
            string id, string token, string owner, string repo, string saveName)
        {
            PlayerId    = id.Trim().ToLower();
            GitHubToken = token.Trim();
            RepoOwner   = owner.Trim();
            RepoName    = repo.Trim();
            SaveName    = saveName.Trim();
            Save();

            ScreenMessages.PostScreenMessage(
                $"[KSP CLUB] Settings saved. Player ID: '{PlayerId}'",
                4f, ScreenMessageStyle.UPPER_CENTER);
            Debug.Log($"[KSPClub] Config saved: playerId={PlayerId} repo={RepoOwner}/{RepoName}");
        }

        public void SetPlayerId(string id)
        {
            PlayerId = id.Trim().ToLower();
            Save();
        }

        public GitHubClient MakeClient() =>
            new GitHubClient(GitHubToken, RepoOwner, RepoName);

        // ------------------------------------------------------------------ persistence

        void Load()
        {
            if (!File.Exists(ConfigPath)) return;
            ConfigNode node = ConfigNode.Load(ConfigPath);
            if (node == null) return;

            PlayerId       = node.GetValue("playerId")      ?? "";
            GitHubToken    = node.GetValue("githubToken")   ?? "";
            RepoOwner      = node.GetValue("repoOwner")     ?? "wadeolsson";
            RepoName       = node.GetValue("repoName")      ?? "ksp-club-saves";
            SaveName       = node.GetValue("saveName")      ?? "KSP_CLUB";
            _lastOutputSha = node.GetValue("lastOutputSha") ?? "";

            if (!string.IsNullOrEmpty(PlayerId))
                Debug.Log($"[KSPClub] Config loaded: playerId={PlayerId} sync={SyncConfigured}");
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            ConfigNode node = new ConfigNode("KSPCLUB_PLAYER");
            node.AddValue("playerId",      PlayerId);
            node.AddValue("githubToken",   GitHubToken);
            node.AddValue("repoOwner",     RepoOwner);
            node.AddValue("repoName",      RepoName);
            node.AddValue("saveName",      SaveName);
            node.AddValue("lastOutputSha", _lastOutputSha);
            node.Save(ConfigPath);
        }

        // ------------------------------------------------------------------ utilities

        private static void ShowError(string message)
        {
            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog("KSPClubError", message, "KSP CLUB — Error",
                    HighLogic.UISkin,
                    new DialogGUIButton("OK", null, true)),
                false, HighLogic.UISkin);
        }

        private IEnumerator DelayThen(float seconds, Action action)
        {
            if (seconds > 0f) yield return new WaitForSeconds(seconds);
            else              yield return null;
            action();
        }
    }
}
