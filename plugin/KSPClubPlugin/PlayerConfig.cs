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
        public string ColorName   { get; private set; } = "blue";
        public Color  PlayerColor => OrbitColorsBase.Presets.TryGetValue(ColorName, out var c)
                                     ? c : OrbitColorsBase.Presets["blue"];

        // GitHub sync settings
        public string GitHubToken { get; private set; } = "";
        public string RepoOwner   { get; private set; } = "wadeolsson";
        public string RepoName    { get; private set; } = "ksp-club-saves";
        public string SaveName    { get; private set; } = "KSP_CLUB";

        private string _lastOutputSha    = "";
        private string _lastNewsWeek     = "";
        private bool   _checkedThisSession;

        private static string NewsPath =>
            Path.Combine(KSPUtil.ApplicationRootPath,
                "GameData/KSPClubPlugin/PluginData/news.json");

        // Latest news loaded from disk: list of "text" strings for current week
        public static System.Collections.Generic.List<string> LatestNews
            = new System.Collections.Generic.List<string>();

        // Pending game node cached from onGameStateLoad, processed after scene settles
        private ConfigNode? _pendingGameNode;

        // Diplomatic relations toward other players
        private readonly System.Collections.Generic.Dictionary<string, Relation> _relations
            = new System.Collections.Generic.Dictionary<string, Relation>();

        // Other players discovered from save data: playerId → agencyName
        public static readonly System.Collections.Generic.Dictionary<string, string> KnownPlayers
            = new System.Collections.Generic.Dictionary<string, string>();

        public Relation GetRelation(string playerId)
        {
            if (playerId == PlayerId) return Relation.Friendly; // always friendly with yourself
            return _relations.TryGetValue(playerId, out var r) ? r : Relation.Neutral;
        }

        public void SetRelation(string playerId, Relation relation)
        {
            _relations[playerId] = relation;
            Save();
            Debug.Log($"[KSPClub] Relation toward '{playerId}' set to {relation}");
        }

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
            LoadNews();
            GameEvents.onLevelWasLoaded.Add(OnSceneLoaded);
            GameEvents.onGameStateSave.Add(OnGameStateSave);
            GameEvents.onGameStateLoad.Add(OnGameStateLoad);
            GameEvents.onProtoVesselSave.Add(OnProtoVesselSave);
        }

        void OnDestroy()
        {
            GameEvents.onLevelWasLoaded.Remove(OnSceneLoaded);
            GameEvents.onGameStateSave.Remove(OnGameStateSave);
            GameEvents.onGameStateLoad.Remove(OnGameStateLoad);
            GameEvents.onProtoVesselSave.Remove(OnProtoVesselSave);
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

            // --- auto-show news on Space Center entry if this week's news is new
            if (scene == GameScenes.SPACECENTER &&
                LatestNews.Count > 0 && _lastNewsWeek != "shown")
            {
                _lastNewsWeek = "shown";
                Save();
                StartCoroutine(DelayThen(1.5f, SaveSyncUI.ShowNewsStatic));
            }
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
            // Also populate the orbit color cache for all vessels
            var flightState = gameNode.GetNode("FLIGHTSTATE");
            if (flightState != null)
            {
                foreach (ConfigNode vesselNode in flightState.GetNodes("VESSEL"))
                {
                    string vid      = vesselNode.GetValue("playerID") ?? "";
                    string colorStr = vesselNode.GetValue("playerColor") ?? "";
                    uint   pid      = 0;
                    uint.TryParse(vesselNode.GetValue("persistentId"), out pid);

                    // Cache vessel color for OrbitColors
                    if (pid != 0 && !string.IsNullOrEmpty(colorStr))
                    {
                        Color c = OrbitColorsBase.ParseColor(colorStr);
                        if (c != Color.clear)
                            OrbitColorsBase.VesselColorCache[pid] = c;
                    }

                    // Cache vessel owner + discover other players
                    if (pid != 0 && !string.IsNullOrEmpty(vid))
                    {
                        OrbitColorsBase.VesselOwnerCache[pid] = vid;
                        if (vid != PlayerId)
                        {
                            string agency = vesselNode.GetValue("agencyName") ?? "";
                            KnownPlayers[vid] = agency;
                        }
                    }

                    // Cache tanker config for other players' tanker vessels
                    if (pid != 0 && vesselNode.GetValue("isTanker") == "true")
                    {
                        var tc = TankerConfig.Load(vesselNode);
                        tc.Active = true;
                        FuelTanker.TankerCache[pid] = tc;
                    }

                    if (vid == PlayerId || vid == "")
                    {
                        if (pid != 0)
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

        // ------------------------------------------------------------------ Fix 2: stamp playerID + agencyName

        // Fires per-vessel during serialization — the right moment to inject fields.
        void OnProtoVesselSave(GameEvents.FromToAction<ProtoVessel, ConfigNode> data)
        {
            if (string.IsNullOrEmpty(PlayerId)) return;

            var scenario = KSPClubScenario.Instance;
            if (scenario == null) return;

            if (!scenario.OwnsVessel(data.from.persistentId)) return;

            // Check for a pending transfer — stamp target instead of self
            if (VesselTrading.PendingTransfers.TryGetValue(
                    data.from.persistentId, out string transferTarget))
            {
                data.to.RemoveValue("playerID");
                data.to.AddValue("playerID", PlayerId);  // still ours until merge
                data.to.RemoveValue("transferTarget");
                data.to.AddValue("transferTarget", transferTarget);
                data.to.RemoveValue("playerColor");
                data.to.AddValue("playerColor", OrbitColorsBase.ColorToString(PlayerColor));
                VesselTrading.PendingTransfers.Remove(data.from.persistentId);
                Debug.Log($"[KSPClub] Stamped transfer on '{data.from.vesselName}' → {transferTarget}");
                return;
            }

            data.to.RemoveValue("playerID");
            data.to.AddValue("playerID", PlayerId);

            if (!string.IsNullOrEmpty(AgencyName))
            {
                data.to.RemoveValue("agencyName");
                data.to.AddValue("agencyName", AgencyName);
            }

            data.to.RemoveValue("playerColor");
            data.to.AddValue("playerColor", OrbitColorsBase.ColorToString(PlayerColor));

            // Stamp tanker config so other players see this vessel is a tanker
            var tankerCfg = KSPClubScenario.Instance?.GetTankerConfig(data.from.persistentId);
            if (tankerCfg != null && tankerCfg.Active)
            {
                data.to.RemoveValue("isTanker");
                data.to.AddValue("isTanker", "true");
                tankerCfg.Save(data.to);
            }
            else
            {
                data.to.RemoveValue("isTanker");
            }

            Debug.Log($"[KSPClub] Stamped vessel '{data.from.vesselName}' " +
                      $"playerID={PlayerId} color={ColorName}" +
                      (tankerCfg?.Active == true ? " [TANKER]" : ""));
        }

        // Stamps Kerbals — roster is available in the full game node.
        void OnGameStateSave(ConfigNode gameNode)
        {
            if (string.IsNullOrEmpty(PlayerId)) return;

            var scenario = KSPClubScenario.Instance;
            if (scenario == null) return;

            var roster = gameNode.GetNode("ROSTER");
            if (roster == null) return;

            int tagged = 0;
            foreach (ConfigNode kerbalNode in roster.GetNodes("KERBAL"))
            {
                string name = kerbalNode.GetValue("name") ?? "";
                if (!scenario.OwnsKerbal(name)) continue;

                kerbalNode.RemoveValue("playerID");
                kerbalNode.AddValue("playerID", PlayerId);

                if (!string.IsNullOrEmpty(AgencyName))
                {
                    kerbalNode.RemoveValue("agencyName");
                    kerbalNode.AddValue("agencyName", AgencyName);
                }
                tagged++;
            }

            if (tagged > 0)
                Debug.Log($"[KSPClub] Stamped {tagged} Kerbal(s) with playerID={PlayerId}");
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

            // Also download the news feed
            StartCoroutine(DownloadNews(client));

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

        // ------------------------------------------------------------------ news feed

        IEnumerator DownloadNews(GitHubClient client)
        {
            byte[]? data = null;
            yield return client.DownloadFile("news/latest.json", bytes => data = bytes);
            if (data == null) yield break;

            try
            {
                File.WriteAllBytes(NewsPath, data);
                LoadNews();
                Debug.Log($"[KSPClub] News downloaded: {LatestNews.Count} event(s)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KSPClub] Could not write news: {ex.Message}");
            }
        }

        public void LoadNews()
        {
            LatestNews.Clear();
            if (!File.Exists(NewsPath)) return;
            try
            {
                string json = File.ReadAllText(NewsPath, System.Text.Encoding.UTF8);
                // Simple JSON parse — extract "text" values and "week"
                string week = ParseJsonString(json, "week") ?? "";
                _lastNewsWeek = week;

                // Extract all "text": "..." values in order
                string search = json;
                const string key = "\"text\":\"";
                int idx;
                while ((idx = search.IndexOf(key, System.StringComparison.Ordinal)) >= 0)
                {
                    idx += key.Length;
                    int end = search.IndexOf('"', idx);
                    if (end < 0) break;
                    string text = search.Substring(idx, end - idx)
                        .Replace("\\u0027", "'").Replace("\\\"", "\"");
                    LatestNews.Add(text);
                    search = search.Substring(end + 1);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KSPClub] Could not parse news: {ex.Message}");
            }
        }

        static string? ParseJsonString(string json, string field)
        {
            string key = $"\"{field}\":\"";
            int start  = json.IndexOf(key, System.StringComparison.Ordinal);
            if (start < 0) return null;
            start += key.Length;
            int end = json.IndexOf('"', start);
            return end < 0 ? null : json.Substring(start, end - start);
        }

        // ------------------------------------------------------------------ setup dialog

        public void ShowSetupDialog()
        {
            string inputId     = PlayerId;
            string inputAgency = AgencyName;
            string inputColor  = ColorName;
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
                    new DialogGUILabel("<b>Orbit Color</b>  (blue/red/green/orange/purple/yellow/cyan/pink)"),
                    new DialogGUITextInput(inputColor, "blue", false, 16,
                        s => { inputColor = s; return s; }, 28f),
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
                        SetConfig(inputId, inputAgency, inputColor, inputToken,
                                  inputOwner, inputRepo, inputSave)),
                    new DialogGUIButton("Cancel", null, false)
                ),
                false, HighLogic.UISkin);
        }

        // ------------------------------------------------------------------ config management

        public void SetConfig(string id, string agency, string color, string token,
                              string owner, string repo, string saveName)
        {
            PlayerId    = id.Trim().ToLower();
            AgencyName  = agency.Trim();
            ColorName   = OrbitColorsBase.Presets.ContainsKey(color.Trim().ToLower())
                          ? color.Trim().ToLower() : "blue";
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

        /// <summary>
        /// Post-process a saved .sfs file to stamp playerID + agencyName into
        /// KERBAL nodes. Uses KSP's own ConfigNode parser so no custom parser needed.
        /// Call this after GamePersistence.SaveGame has written the file to disk.
        /// </summary>
        public void StampKerbalsInFile(string filePath)
        {
            if (string.IsNullOrEmpty(PlayerId) || !File.Exists(filePath)) return;

            var scenario = KSPClubScenario.Instance;
            if (scenario == null || scenario.OwnedKerbalCount == 0) return;

            try
            {
                ConfigNode root = ConfigNode.Load(filePath);
                if (root == null) return;

                ConfigNode? game   = root.GetNode("GAME");
                ConfigNode? roster = game?.GetNode("ROSTER");
                if (roster == null) return;

                int tagged = 0;
                foreach (ConfigNode kerbalNode in roster.GetNodes("KERBAL"))
                {
                    string name = kerbalNode.GetValue("name") ?? "";
                    if (!scenario.OwnsKerbal(name)) continue;

                    kerbalNode.RemoveValue("playerID");
                    kerbalNode.AddValue("playerID", PlayerId);

                    if (!string.IsNullOrEmpty(AgencyName))
                    {
                        kerbalNode.RemoveValue("agencyName");
                        kerbalNode.AddValue("agencyName", AgencyName);
                    }
                    tagged++;
                }

                if (tagged > 0)
                {
                    root.Save(filePath);
                    Debug.Log($"[KSPClub] Stamped {tagged} Kerbal(s) in file with " +
                              $"playerID={PlayerId} agencyName={AgencyName}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KSPClub] StampKerbalsInFile failed: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------ persistence

        void Load()
        {
            if (!File.Exists(ConfigPath)) return;
            ConfigNode node = ConfigNode.Load(ConfigPath);
            if (node == null) return;

            PlayerId       = node.GetValue("playerId")      ?? "";
            AgencyName     = node.GetValue("agencyName")    ?? "";
            ColorName      = node.GetValue("colorName")     ?? "blue";
            GitHubToken    = node.GetValue("githubToken")   ?? "";
            RepoOwner      = node.GetValue("repoOwner")     ?? "wadeolsson";
            RepoName       = node.GetValue("repoName")      ?? "ksp-club-saves";
            SaveName       = node.GetValue("saveName")      ?? "KSP_CLUB";
            _lastOutputSha = node.GetValue("lastOutputSha") ?? "";
            _lastNewsWeek  = node.GetValue("lastNewsWeek")  ?? "";

            _relations.Clear();
            ConfigNode? relNode = node.GetNode("RELATIONS");
            if (relNode != null)
                foreach (ConfigNode.Value v in relNode.values)
                    if (System.Enum.TryParse<Relation>(v.value, out var r))
                        _relations[v.name] = r;

            if (!string.IsNullOrEmpty(PlayerId))
                Debug.Log($"[KSPClub] Config loaded: {PlayerId} / {AgencyName} " +
                          $"({_relations.Count} relation(s))");
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            ConfigNode node = new ConfigNode("KSPCLUB_PLAYER");
            node.AddValue("playerId",      PlayerId);
            node.AddValue("agencyName",    AgencyName);
            node.AddValue("colorName",     ColorName);
            node.AddValue("githubToken",   GitHubToken);
            node.AddValue("repoOwner",     RepoOwner);
            node.AddValue("repoName",      RepoName);
            node.AddValue("saveName",      SaveName);
            node.AddValue("lastOutputSha", _lastOutputSha);
            node.AddValue("lastNewsWeek",  _lastNewsWeek);
            if (_relations.Count > 0)
            {
                ConfigNode relNode = node.AddNode("RELATIONS");
                foreach (var kvp in _relations)
                    relNode.AddValue(kvp.Key, kvp.Value.ToString());
            }
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
