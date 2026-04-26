using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace KSPClub
{
    /// <summary>
    /// Persists across all scene loads (persist=true). Responsibilities:
    ///   - Load/save player config (ID, agency name, GitHub token, repo settings)
    ///   - Show first-run setup dialog
    ///   - On game load: claim existing untagged vessels + Kerbals as this player's
    ///   - On game save: stamp playerID + agencyName into owned VESSEL and KERBAL nodes
    ///   - Check for a new merged save on main menu load and offer to download it
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class PlayerConfig : MonoBehaviour
    {
        public static PlayerConfig Instance { get; private set; } = null!;

        // player identity
        public string PlayerId    { get; private set; } = "";
        public string AgencyName  { get; private set; } = "";

        // GitHub sync settings
        public string GitHubToken { get; private set; } = "";
        public string RepoOwner   { get; private set; } = "wadeolsson";
        public string RepoName    { get; private set; } = "ksp-club-saves";
        public string SaveName    { get; private set; } = "KSP_CLUB";

        private string _lastOutputSha   = "";
        private bool   _checkedThisSession;

        // Pending game node cached from onGameStateLoad, processed after scene settles
        private ConfigNode? _pendingGameNode;

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
            GameEvents.onGameStateLoad.Add(OnGameStateLoad);
        }

        void OnDestroy()
        {
            GameEvents.onLevelWasLoaded.Remove(OnSceneLoaded);
            GameEvents.onGameStateSave.Remove(OnGameStateSave);
            GameEvents.onGameStateLoad.Remove(OnGameStateLoad);
        }

        // ------------------------------------------------------------------ game state load

        // Cache the raw ConfigNode as the game loads so we can sweep it for
        // untagged vessels/Kerbals once the scene (and ClubScenario) are ready.
        void OnGameStateLoad(ConfigNode gameNode)
        {
            _pendingGameNode = gameNode;
        }

        // ------------------------------------------------------------------ scene hook

        void OnSceneLoaded(GameScenes scene)
        {
            // --- claim pass: sweep pending game node now that ClubScenario is live
            if (_pendingGameNode != null &&
                (scene == GameScenes.SPACECENTER || scene == GameScenes.FLIGHT))
            {
                ClaimExistingFromNode(_pendingGameNode);
                _pendingGameNode = null;
            }

            // --- main menu: setup dialog + new-save check
            if (scene == GameScenes.MAINMENU)
            {
                if (string.IsNullOrEmpty(PlayerId) ||
                    string.IsNullOrEmpty(GitHubToken) ||
                    string.IsNullOrEmpty(AgencyName))
                    StartCoroutine(DelayThen(2f, ShowSetupDialog));
                else if (!_checkedThisSession && SyncConfigured)
                {
                    _checkedThisSession = true;
                    StartCoroutine(DelayThen(2f, CheckForNewSave));
                }
            }

            // --- first entry to active scene with no ID yet
            if (string.IsNullOrEmpty(PlayerId) &&
                (scene == GameScenes.SPACECENTER || scene == GameScenes.FLIGHT))
                StartCoroutine(DelayThen(0f, ShowSetupDialog));
        }

        // ------------------------------------------------------------------ Fix 1: claim existing vessels + Kerbals

        void ClaimExistingFromNode(ConfigNode gameNode)
        {
            if (string.IsNullOrEmpty(PlayerId)) return;

            var scenario = KSPClubScenario.Instance;
            if (scenario == null) return;

            int vesselsClaimed = 0;
            int kerbalsClaimed = 0;

            // Vessels: claim those tagged as ours or with no tag at all
            var flightState = gameNode.GetNode("FLIGHTSTATE");
            if (flightState != null)
            {
                foreach (ConfigNode vesselNode in flightState.GetNodes("VESSEL"))
                {
                    string vid = vesselNode.GetValue("playerID") ?? "";
                    if (vid == PlayerId || vid == "")
                    {
                        if (uint.TryParse(vesselNode.GetValue("persistentId"), out uint pid))
                        {
                            scenario.ClaimVessel(pid);
                            vesselsClaimed++;
                        }
                    }
                }
            }

            // Kerbals: claim those tagged as ours or with no tag (excluding stock)
            var roster = gameNode.GetNode("ROSTER");
            if (roster != null)
            {
                foreach (ConfigNode kerbalNode in roster.GetNodes("KERBAL"))
                {
                    string name = kerbalNode.GetValue("name") ?? "";
                    string kid  = kerbalNode.GetValue("playerID") ?? "";

                    if (string.IsNullOrEmpty(name)) continue;
                    if (KerbalRestrictor.IsStockKerbal(name)) continue;

                    if (kid == PlayerId || kid == "")
                    {
                        scenario.ClaimKerbal(name);
                        kerbalsClaimed++;
                    }
                }
            }

            if (vesselsClaimed > 0 || kerbalsClaimed > 0)
                Debug.Log($"[KSPClub] Claimed {vesselsClaimed} vessel(s) and " +
                          $"{kerbalsClaimed} Kerbal(s) from existing save.");
        }

        // ------------------------------------------------------------------ Fix 2: save hook — stamp playerID + agencyName

        void OnGameStateSave(ConfigNode gameNode)
        {
            if (string.IsNullOrEmpty(PlayerId)) return;

            var scenario = KSPClubScenario.Instance;
            if (scenario == null) return;

            int vesselTagged = 0;
            int kerbalTagged = 0;

            // Tag owned vessels
            var flightState = gameNode.GetNode("FLIGHTSTATE");
            if (flightState != null)
            {
                foreach (ConfigNode vesselNode in flightState.GetNodes("VESSEL"))
                {
                    if (uint.TryParse(vesselNode.GetValue("persistentId"), out uint pid) &&
                        scenario.OwnsVessel(pid))
                    {
                        vesselNode.RemoveValue("playerID");
                        vesselNode.AddValue("playerID", PlayerId);

                        if (!string.IsNullOrEmpty(AgencyName))
                        {
                            vesselNode.RemoveValue("agencyName");
                            vesselNode.AddValue("agencyName", AgencyName);
                        }
                        vesselTagged++;
                    }
                }
            }

            // Tag owned Kerbals
            var roster = gameNode.GetNode("ROSTER");
            if (roster != null)
            {
                foreach (ConfigNode kerbalNode in roster.GetNodes("KERBAL"))
                {
                    string name = kerbalNode.GetValue("name") ?? "";
                    if (scenario.OwnsKerbal(name))
                    {
                        kerbalNode.RemoveValue("playerID");
                        kerbalNode.AddValue("playerID", PlayerId);

                        if (!string.IsNullOrEmpty(AgencyName))
                        {
                            kerbalNode.RemoveValue("agencyName");
                            kerbalNode.AddValue("agencyName", AgencyName);
                        }
                        kerbalTagged++;
                    }
                }
            }

            if (vesselTagged > 0 || kerbalTagged > 0)
                Debug.Log($"[KSPClub] Stamped {vesselTagged} vessel(s) and " +
                          $"{kerbalTagged} Kerbal(s) with playerID={PlayerId}");
        }

        // ------------------------------------------------------------------ new-save check

        public bool SyncConfigured =>
            !string.IsNullOrEmpty(PlayerId) &&
            !string.IsNullOrEmpty(GitHubToken) &&
            !string.IsNullOrEmpty(RepoOwner) &&
            !string.IsNullOrEmpty(RepoName);

        void CheckForNewSave() => StartCoroutine(CheckForNewSaveCoroutine());

        IEnumerator CheckForNewSaveCoroutine()
        {
            var client = MakeClient();
            string? remoteSha = null;
            yield return client.GetSha($"output/{PlayerId}/persistent.sfs",
                sha => remoteSha = sha);

            if (remoteSha == null || remoteSha == _lastOutputSha) yield break;

            Debug.Log($"[KSPClub] New output save available (sha={remoteSha}).");
            ShowNewSaveDialog(client, remoteSha);
        }

        void ShowNewSaveDialog(GitHubClient client, string remoteSha)
        {
            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    "KSPClubNewSave",
                    $"The game master has merged this week's saves.\n\n" +
                    $"A new <b>{SaveName}</b> save is ready to download.\n\n" +
                    "Download now and load it when you start playing?",
                    "KSP CLUB — New Save Available",
                    HighLogic.UISkin, 360f,
                    new DialogGUIButton("Download", () =>
                        StartCoroutine(DownloadNewSave(client, remoteSha))),
                    new DialogGUIButton("Later", null, false)
                ),
                false, HighLogic.UISkin);
        }

        IEnumerator DownloadNewSave(GitHubClient client, string newSha)
        {
            ScreenMessages.PostScreenMessage(
                "[KSP CLUB] Downloading new save...", 60f, ScreenMessageStyle.UPPER_CENTER);

            byte[]? data = null;
            yield return client.DownloadFile(
                $"output/{PlayerId}/persistent.sfs", bytes => data = bytes);

            ScreenMessages.PostScreenMessage("", 0f, ScreenMessageStyle.UPPER_CENTER);

            if (data == null)
            {
                ShowError("Download failed. Check your internet connection and try again.");
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

            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    "KSPClubDownloadDone",
                    $"Save downloaded!\n\nLoad the <b>{SaveName}</b> save to play " +
                    "with this week's universe.",
                    "KSP CLUB — Download Complete",
                    HighLogic.UISkin,
                    new DialogGUIButton("OK", null, true)
                ),
                false, HighLogic.UISkin);
        }

        // ------------------------------------------------------------------ setup dialog

        public void ShowSetupDialog()
        {
            string inputId     = PlayerId;
            string inputAgency = AgencyName;
            string inputToken  = GitHubToken;
            string inputOwner  = RepoOwner;
            string inputRepo   = RepoName;
            string inputSave   = SaveName;

            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    "KSPClubSetup",
                    string.IsNullOrEmpty(PlayerId)
                        ? "Welcome to KSP CLUB!\n\nConfigure your player ID and GitHub " +
                          "token to enable automatic save sync."
                        : "Update your KSP CLUB settings.",
                    "KSP CLUB — Setup",
                    HighLogic.UISkin, 400f,
                    new DialogGUILabel("<b>Player ID</b>  (assigned by your game master)"),
                    new DialogGUITextInput(inputId, "e.g. wade", false, 32,
                        s => { inputId = s; return s; }, 28f),
                    new DialogGUILabel("<b>Agency Name</b>  (your space agency name)"),
                    new DialogGUITextInput(inputAgency, "e.g. Olsson Aerospace", false, 64,
                        s => { inputAgency = s; return s; }, 28f),
                    new DialogGUILabel("<b>GitHub Token</b>  (fine-grained PAT, repo Contents R/W)"),
                    new DialogGUITextInput(inputToken, "github_pat_...", true, 200,
                        s => { inputToken = s; return s; }, 28f),
                    new DialogGUILabel("<b>Repo Owner / Repo Name</b>"),
                    new DialogGUIHorizontalLayout(
                        new DialogGUITextInput(inputOwner, "owner", false, 64,
                            s => { inputOwner = s; return s; }, 28f),
                        new DialogGUILabel("  /  "),
                        new DialogGUITextInput(inputRepo, "ksp-club-saves", false, 64,
                            s => { inputRepo = s; return s; }, 28f)
                    ),
                    new DialogGUILabel("<b>Club Save Name</b>  (KSP save folder)"),
                    new DialogGUITextInput(inputSave, "KSP_CLUB", false, 32,
                        s => { inputSave = s; return s; }, 28f),
                    new DialogGUIButton("Save", () =>
                        SetConfig(inputId, inputAgency, inputToken,
                                  inputOwner, inputRepo, inputSave)),
                    new DialogGUIButton("Cancel", null, false)
                ),
                false, HighLogic.UISkin);
        }

        // ------------------------------------------------------------------ config management

        public void SetConfig(string id, string agency, string token,
                              string owner, string repo, string saveName)
        {
            PlayerId    = id.Trim().ToLower();
            AgencyName  = agency.Trim();
            GitHubToken = token.Trim();
            RepoOwner   = owner.Trim();
            RepoName    = repo.Trim();
            SaveName    = saveName.Trim();
            Save();

            ScreenMessages.PostScreenMessage(
                $"[KSP CLUB] Settings saved — {PlayerId} / {AgencyName}",
                4f, ScreenMessageStyle.UPPER_CENTER);
            Debug.Log($"[KSPClub] Config: playerId={PlayerId} agency={AgencyName} " +
                      $"repo={RepoOwner}/{RepoName}");
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
            AgencyName     = node.GetValue("agencyName")    ?? "";
            GitHubToken    = node.GetValue("githubToken")   ?? "";
            RepoOwner      = node.GetValue("repoOwner")     ?? "wadeolsson";
            RepoName       = node.GetValue("repoName")      ?? "ksp-club-saves";
            SaveName       = node.GetValue("saveName")      ?? "KSP_CLUB";
            _lastOutputSha = node.GetValue("lastOutputSha") ?? "";

            if (!string.IsNullOrEmpty(PlayerId))
                Debug.Log($"[KSPClub] Config loaded: {PlayerId} / {AgencyName}");
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            ConfigNode node = new ConfigNode("KSPCLUB_PLAYER");
            node.AddValue("playerId",      PlayerId);
            node.AddValue("agencyName",    AgencyName);
            node.AddValue("githubToken",   GitHubToken);
            node.AddValue("repoOwner",     RepoOwner);
            node.AddValue("repoName",      RepoName);
            node.AddValue("saveName",      SaveName);
            node.AddValue("lastOutputSha", _lastOutputSha);
            node.Save(ConfigPath);
        }

        // ------------------------------------------------------------------ utilities

        internal static void ShowError(string message)
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
