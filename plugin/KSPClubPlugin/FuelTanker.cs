using System;
using System.Collections;
using System.Collections.Generic;
using KSP.UI.Screens;
using UnityEngine;

namespace KSPClub
{
    /// <summary>
    /// In-flight fuel tanker system.
    ///
    /// Own vessel menu  — toggle tanker status, set prices and reserve.
    /// Nearby tanker    — view prices, start/stop pumping, full refuel.
    ///
    /// Range: 50 m in orbit/flight, 500 m when landed/splashed.
    /// Pump rate: 50 units/second.
    ///
    /// Transactions are recorded in KSPClubScenario and processed by the
    /// merger on the next weekly cycle (funds credited/debited, tanker fuel
    /// reduced in the universal state).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class FuelTanker : MonoBehaviour
    {
        // ------------------------------------------------------------------ constants

        public  const float PUMP_RATE        = 50f;   // units per second
        private const float ORBIT_RANGE      = 50f;   // metres, in flight/orbit
        private const float SURFACE_RANGE    = 500f;  // metres, when landed/splashed

        private static readonly string[] RESOURCES =
            { "LiquidFuel", "Oxidizer", "MonoPropellant" };

        // ------------------------------------------------------------------ state

        private ApplicationLauncherButton? _button;
        private bool        _isPumping;
        private Coroutine?  _pumpRoutine;

        private static Texture2D? _tankerIcon;

        // Cache of other players' tanker configs populated by PlayerConfig on save load
        public static readonly Dictionary<uint, TankerConfig> TankerCache
            = new Dictionary<uint, TankerConfig>();

        // ------------------------------------------------------------------ lifecycle

        void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            _tankerIcon = GameDatabase.Instance?.GetTexture("KSPClubPlugin/icon_tanker", false);
        }

        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            if (_button != null)
                ApplicationLauncher.Instance.RemoveModApplication(_button);
            StopPump();
        }

        // ------------------------------------------------------------------ toolbar

        void AddButton()
        {
            if (_button != null) return;
            _button = ApplicationLauncher.Instance.AddModApplication(
                OnButtonClick, OnButtonClick,
                null, null, null, null,
                ApplicationLauncher.AppScenes.FLIGHT,
                MakeIcon()
            );
        }

        void OnButtonClick()
        {
            _button?.SetFalse(false);
            ShowFuelMenu();
        }

        // ------------------------------------------------------------------ main menu

        void ShowFuelMenu()
        {
            var myVessel = FlightGlobals.ActiveVessel;
            var scenario = KSPClubScenario.Instance;
            var cfg      = PlayerConfig.Instance;
            if (myVessel == null || scenario == null || cfg == null) return;

            bool isMine   = scenario.OwnsVessel(myVessel.persistentId);
            var  nearbyTankers = FindNearbyTankers(myVessel);

            // Build dialog body
            string body = $"<b>Vessel:</b> {myVessel.vesselName}\n";
            if (isMine)
            {
                bool active = scenario.IsTanker(myVessel.persistentId);
                body += active
                    ? "Status: <b>● Tanker ACTIVE</b>"
                    : "Status: ○ Not a tanker";
            }

            var elements = new List<DialogGUIBase>();
            elements.Add(new DialogGUILabel(body));

            // --- own vessel section ---
            if (isMine)
            {
                bool active = scenario.IsTanker(myVessel.persistentId);
                if (active)
                {
                    elements.Add(new DialogGUIButton("⚙ Tanker Settings",
                        () => ShowTankerSettingsDialog(myVessel), false));
                    elements.Add(new DialogGUIButton("Deactivate Tanker",
                        () => { DeactivateTanker(myVessel); ShowFuelMenu(); }, true));
                }
                else
                {
                    elements.Add(new DialogGUIButton("Set This Vessel as Tanker",
                        () => { ActivateTanker(myVessel); ShowFuelMenu(); }, true));
                }
            }

            // --- nearby tankers section (always shown) ---
            elements.Add(new DialogGUILabel("\n<b>Nearby Tankers:</b>"));
            if (nearbyTankers.Count > 0)
            {
                foreach (var nt in nearbyTankers)
                {
                    var    tv    = nt.Vessel;
                    var    tc    = nt.Config;
                    string dist  = Distance(myVessel, tv).ToString("F0");
                    string label = $"{tv.vesselName}  ({tc.OwnerAgency})  {dist}m";
                    elements.Add(new DialogGUIButton(label,
                        () => ShowRefuelDialog(tv, tc), true));
                }
            }
            else
            {
                elements.Add(new DialogGUILabel("No tankers within range.\n(50m orbital / 500m landed)"));
            }

            elements.Add(new DialogGUIButton("Close", null, true));

            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog("KSPClubFuel", "",
                    "KSP CLUB — Fuel System",
                    HighLogic.UISkin, 420f,
                    elements.ToArray()),
                false, HighLogic.UISkin);
        }

        // ------------------------------------------------------------------ tanker settings

        void ShowTankerSettingsDialog(Vessel vessel)
        {
            var scenario = KSPClubScenario.Instance;
            var cfg      = PlayerConfig.Instance;
            if (scenario == null || cfg == null) return;

            TankerConfig tanker = scenario.GetTankerConfig(vessel.persistentId)
                                  ?? new TankerConfig { Active = true };

            string inputReserve   = (tanker.ReservePct       * 100f).ToString("F0");
            string inputDiscount  = (tanker.FriendlyDiscount  * 100f).ToString("F0");
            string inputLF        = tanker.Prices["LiquidFuel"].ToString("F1");
            string inputOx        = tanker.Prices["Oxidizer"].ToString("F1");
            string inputMP        = tanker.Prices["MonoPropellant"].ToString("F1");

            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog("KSPClubTankerSettings", "",
                    $"Tanker Settings — {vessel.vesselName}",
                    HighLogic.UISkin, 400f,

                    new DialogGUILabel("<b>Reserve %</b>  (never sell below this)"),
                    new DialogGUITextInput(inputReserve, "e.g. 20", false, 6,
                        s => { inputReserve  = s; return s; }, 28f),

                    new DialogGUILabel("<b>Friendly Discount %</b>  (0 = full price, 100 = free)"),
                    new DialogGUITextInput(inputDiscount, "0", false, 6,
                        s => { inputDiscount = s; return s; }, 28f),

                    new DialogGUILabel("<b>Price per unit (◆ funds)</b>"),
                    new DialogGUIHorizontalLayout(
                        new DialogGUILabel("LiquidFuel:"),
                        new DialogGUITextInput(inputLF, "10.0", false, 8,
                            s => { inputLF = s; return s; }, 28f)),
                    new DialogGUIHorizontalLayout(
                        new DialogGUILabel("Oxidizer:"),
                        new DialogGUITextInput(inputOx, "8.0", false, 8,
                            s => { inputOx = s; return s; }, 28f)),
                    new DialogGUIHorizontalLayout(
                        new DialogGUILabel("MonoProp:"),
                        new DialogGUITextInput(inputMP, "25.0", false, 8,
                            s => { inputMP = s; return s; }, 28f)),

                    new DialogGUIButton("Save Settings", () =>
                    {
                        tanker.Active           = true;
                        tanker.OwnerPlayerId    = cfg.PlayerId;
                        tanker.OwnerAgency      = cfg.AgencyName;
                        tanker.ReservePct       = ParsePct(inputReserve,   0.20f);
                        tanker.FriendlyDiscount = ParsePct(inputDiscount,  0.00f);
                        tanker.Prices["LiquidFuel"]     = ParseFloat(inputLF, 10f);
                        tanker.Prices["Oxidizer"]       = ParseFloat(inputOx,  8f);
                        tanker.Prices["MonoPropellant"] = ParseFloat(inputMP, 25f);
                        scenario.SetTankerConfig(vessel.persistentId, tanker);
                        ScreenMessages.PostScreenMessage(
                            $"[KSP CLUB] {vessel.vesselName} tanker settings saved.",
                            3f, ScreenMessageStyle.UPPER_CENTER);
                    }),
                    new DialogGUIButton("Close", null, true)
                ),
                false, HighLogic.UISkin);
        }

        // ------------------------------------------------------------------ refuel dialog

        void ShowRefuelDialog(Vessel tankerVessel, TankerConfig tankerCfg)
        {
            var myVessel = FlightGlobals.ActiveVessel;
            var cfg      = PlayerConfig.Instance;
            if (myVessel == null || cfg == null) return;

            float discount = cfg.GetRelation(tankerCfg.OwnerPlayerId) == Relation.Friendly
                             ? tankerCfg.FriendlyDiscount : 0f;

            var elements = new List<DialogGUIBase>();
            elements.Add(new DialogGUILabel(
                $"<b>{tankerVessel.vesselName}</b>  ({tankerCfg.OwnerAgency})\n" +
                $"Distance: {Distance(myVessel, tankerVessel):F1}m\n" +
                $"Relation: {cfg.GetRelation(tankerCfg.OwnerPlayerId)}" +
                (discount > 0 ? $"  → {discount * 100:F0}% discount" : "")
            ));

            float totalCost = 0;
            foreach (var resource in RESOURCES)
            {
                if (!tankerCfg.Prices.TryGetValue(resource, out float basePrice)
                    || basePrice <= 0) continue;

                float price       = basePrice * (1f - discount);
                double tankerHas  = GetResourceAmount(tankerVessel, resource);
                double tankerMax  = GetResourceMax(tankerVessel,    resource);
                double reserve    = tankerMax * tankerCfg.ReservePct;
                double canSell    = Math.Max(0, tankerHas - reserve);
                double myHas      = GetResourceAmount(myVessel, resource);
                double myMax      = GetResourceMax(myVessel,    resource);
                double myNeed     = Math.Max(0, myMax - myHas);
                double toBuy      = Math.Min(canSell, myNeed);
                float  cost       = (float)(toBuy * price);

                totalCost += cost;
                string line = $"{resource}:  {tankerHas:F0}/{tankerMax:F0}u avail" +
                              $"  @  ◆{price:F2}/u" +
                              $"  →  {toBuy:F0}u needed  ◆{cost:F0}";
                elements.Add(new DialogGUILabel(line));
            }

            elements.Add(new DialogGUILabel($"\n<b>Full refuel cost: ◆{totalCost:F0}</b>"));
            elements.Add(new DialogGUILabel($"Pump rate: {PUMP_RATE} u/sec"));

            // Pump buttons
            string activeResource = RESOURCES[0]; // used by Start Pumping
            elements.Add(new DialogGUIHorizontalLayout(
                new DialogGUIButton("Full Refuel", () =>
                {
                    StartCoroutine(FullRefuelCoroutine(tankerVessel, tankerCfg, discount));
                }, true),
                new DialogGUIButton("Start Pumping", () =>
                {
                    if (!_isPumping)
                        _pumpRoutine = StartCoroutine(
                            PumpCoroutine(tankerVessel, tankerCfg, activeResource, double.MaxValue, discount));
                }, false),
                new DialogGUIButton("Stop", () => StopPump(), false)
            ));
            elements.Add(new DialogGUIButton("Close", null, true));

            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog("KSPClubRefuel", "",
                    $"Refuel from {tankerVessel.vesselName}",
                    HighLogic.UISkin, 460f,
                    elements.ToArray()),
                false, HighLogic.UISkin);
        }

        // ------------------------------------------------------------------ pump logic

        IEnumerator FullRefuelCoroutine(Vessel tanker, TankerConfig cfg, float discount)
        {
            foreach (var resource in RESOURCES)
            {
                if (!cfg.Prices.TryGetValue(resource, out float basePrice) || basePrice <= 0) continue;
                yield return StartCoroutine(
                    PumpCoroutine(tanker, cfg, resource, double.MaxValue, discount));
            }
        }

        IEnumerator PumpCoroutine(Vessel tanker, TankerConfig cfg,
                                   string resource, double maxAmount, float discount)
        {
            _isPumping = true;

            if (!cfg.Prices.TryGetValue(resource, out float basePrice) || basePrice <= 0)
            {
                _isPumping = false;
                yield break;
            }

            float  price      = basePrice * (1f - discount);
            double pumped     = 0;
            double totalCost  = 0;

            while (_isPumping)
            {
                // Check range every frame
                if (!InRange(FlightGlobals.ActiveVessel, tanker))
                {
                    ScreenMessages.PostScreenMessage(
                        "[KSP CLUB] Out of range — pumping stopped.", 4f,
                        ScreenMessageStyle.UPPER_CENTER);
                    break;
                }

                // Tanker availability (respect reserve)
                double tankerHas = GetResourceAmount(tanker, resource);
                double tankerMax = GetResourceMax(tanker, resource);
                double canSell   = tankerHas - tankerMax * cfg.ReservePct;
                if (canSell <= 0.1)
                {
                    ScreenMessages.PostScreenMessage(
                        $"[KSP CLUB] {tanker.vesselName} reserve reached.", 4f,
                        ScreenMessageStyle.UPPER_CENTER);
                    break;
                }

                // Buyer capacity
                double myHas   = GetResourceAmount(FlightGlobals.ActiveVessel, resource);
                double myMax   = GetResourceMax(FlightGlobals.ActiveVessel,    resource);
                double mySpace = myMax - myHas;
                if (mySpace <= 0.1)
                {
                    ScreenMessages.PostScreenMessage(
                        $"[KSP CLUB] {resource} tanks full.", 3f,
                        ScreenMessageStyle.UPPER_CENTER);
                    break;
                }

                double delta = Math.Min(PUMP_RATE * Time.deltaTime,
                               Math.Min(canSell,
                               Math.Min(mySpace, maxAmount - pumped)));

                double moved = RemoveFuel(tanker, resource, delta);
                AddFuel(FlightGlobals.ActiveVessel, resource, moved);

                pumped    += moved;
                totalCost += moved * price;

                if (maxAmount != double.MaxValue && pumped >= maxAmount - 0.1) break;

                // Progress message ~every second
                if (Time.frameCount % 60 == 0)
                    ScreenMessages.PostScreenMessage(
                        $"[KSP CLUB] Pumping {resource}  {pumped:F0}u  ◆{totalCost:F0}",
                        1.1f, ScreenMessageStyle.UPPER_RIGHT);

                yield return null;
            }

            _isPumping   = false;
            _pumpRoutine = null;

            if (pumped > 0.5)
            {
                KSPClubScenario.Instance?.RecordTransaction(new TransactionRecord
                {
                    Buyer              = PlayerConfig.Instance?.PlayerId ?? "",
                    Seller             = cfg.OwnerPlayerId,
                    Resource           = resource,
                    Amount             = (float)pumped,
                    TotalCost          = (float)totalCost,
                    TankerPersistentId = tanker.persistentId,
                    Timestamp          = Planetarium.GetUniversalTime(),
                });

                ScreenMessages.PostScreenMessage(
                    $"[KSP CLUB] Fueling complete — {pumped:F0}u {resource}  ◆{totalCost:F0}",
                    6f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        void StopPump()
        {
            _isPumping = false;
            if (_pumpRoutine != null) { StopCoroutine(_pumpRoutine); _pumpRoutine = null; }
        }

        // ------------------------------------------------------------------ tanker activate/deactivate

        void ActivateTanker(Vessel vessel)
        {
            var scenario = KSPClubScenario.Instance;
            var cfg      = PlayerConfig.Instance;
            if (scenario == null || cfg == null) return;

            var existing = scenario.GetTankerConfig(vessel.persistentId)
                           ?? new TankerConfig();
            existing.Active        = true;
            existing.OwnerPlayerId = cfg.PlayerId;
            existing.OwnerAgency   = cfg.AgencyName;
            scenario.SetTankerConfig(vessel.persistentId, existing);

            ScreenMessages.PostScreenMessage(
                $"[KSP CLUB] {vessel.vesselName} is now a fuel tanker!",
                4f, ScreenMessageStyle.UPPER_CENTER);
        }

        void DeactivateTanker(Vessel vessel)
        {
            var scenario = KSPClubScenario.Instance;
            if (scenario == null) return;

            var existing = scenario.GetTankerConfig(vessel.persistentId);
            if (existing == null) return;
            existing.Active = false;
            scenario.SetTankerConfig(vessel.persistentId, existing);

            ScreenMessages.PostScreenMessage(
                $"[KSP CLUB] {vessel.vesselName} tanker deactivated.",
                4f, ScreenMessageStyle.UPPER_CENTER);
        }

        // ------------------------------------------------------------------ proximity

        static float GetRange(Vessel v) =>
            v.Landed || v.Splashed ? SURFACE_RANGE : ORBIT_RANGE;

        static float Distance(Vessel a, Vessel b) =>
            Vector3.Distance(a.transform.position, b.transform.position);

        static bool InRange(Vessel me, Vessel tanker) =>
            Distance(me, tanker) <= GetRange(tanker);

        List<NearbyTanker> FindNearbyTankers(Vessel me)
        {
            var result    = new List<NearbyTanker>();
            var playerCfg = PlayerConfig.Instance;
            var scenario  = KSPClubScenario.Instance;
            if (playerCfg == null) return result;

            foreach (var v in FlightGlobals.VesselsLoaded)
            {
                if (v == me) continue;
                if (!InRange(me, v)) continue;

                TankerConfig? tc = null;

                // Own tanker vessels: config lives in ClubScenario
                if (scenario != null && scenario.OwnsVessel(v.persistentId))
                {
                    tc = scenario.GetTankerConfig(v.persistentId);
                }
                // Other players' tanker vessels: config cached from save data
                else if (TankerCache.TryGetValue(v.persistentId, out var cached))
                {
                    if (playerCfg.GetRelation(cached.OwnerPlayerId) == Relation.Hostile)
                        continue;
                    tc = cached;
                }

                if (tc == null || !tc.Active) continue;
                result.Add(new NearbyTanker { Vessel = v, Config = tc });
            }
            return result;
        }

        class NearbyTanker { public Vessel Vessel = null!; public TankerConfig Config = null!; }

        // ------------------------------------------------------------------ resource helpers

        static double GetResourceAmount(Vessel v, string resource)
        {
            double total = 0;
            foreach (var part in v.Parts)
            {
                var res = part.Resources.Get(resource);
                if (res != null) total += res.amount;
            }
            return total;
        }

        static double GetResourceMax(Vessel v, string resource)
        {
            double total = 0;
            foreach (var part in v.Parts)
            {
                var res = part.Resources.Get(resource);
                if (res != null) total += res.maxAmount;
            }
            return total;
        }

        static double RemoveFuel(Vessel v, string resource, double amount)
        {
            double removed = 0;
            foreach (var part in v.Parts)
            {
                if (removed >= amount - 0.001) break;
                var res = part.Resources.Get(resource);
                if (res == null || res.amount <= 0) continue;
                double take = Math.Min(res.amount, amount - removed);
                res.amount -= take;
                removed    += take;
            }
            return removed;
        }

        static void AddFuel(Vessel v, string resource, double amount)
        {
            double remaining = amount;
            foreach (var part in v.Parts)
            {
                if (remaining <= 0.001) break;
                var res = part.Resources.Get(resource);
                if (res == null) continue;
                double space = res.maxAmount - res.amount;
                if (space <= 0) continue;
                double add = Math.Min(space, remaining);
                res.amount += add;
                remaining  -= add;
            }
        }

        // ------------------------------------------------------------------ icon

        // ------------------------------------------------------------------ map overlay

        void OnGUI()
        {
            if (_tankerIcon == null) return;
            if (!MapView.MapIsEnabled) return;

            var scenario = KSPClubScenario.Instance;

            foreach (var vessel in FlightGlobals.Vessels)
            {
                // Check own tankers via scenario, other players' via cache
                bool isTanker = (scenario != null && scenario.IsTanker(vessel.persistentId))
                                || (TankerCache.TryGetValue(vessel.persistentId, out var tc) && tc.Active);
                if (!isTanker) continue;

                // Project vessel position to screen space (map view uses ScaledSpace)
                Vector3d scaledPos = ScaledSpace.LocalToScaledSpace(vessel.GetWorldPos3D());
                Vector3 screen = PlanetariumCamera.Camera.WorldToScreenPoint(
                    new Vector3((float)scaledPos.x, (float)scaledPos.y, (float)scaledPos.z));

                if (screen.z <= 0) continue; // behind camera

                // KSP GUI uses top-left origin; flip Y
                float sx = screen.x;
                float sy = Screen.height - screen.y;

                const float SIZE = 24f;
                GUI.DrawTexture(new Rect(sx - SIZE / 2, sy - SIZE / 2, SIZE, SIZE), _tankerIcon);
            }
        }

        // ------------------------------------------------------------------ icon

        static Texture2D MakeIcon()
        {
            // Use the tank icon for the toolbar button
            var tex = GameDatabase.Instance?.GetTexture("KSPClubPlugin/icon_tanker", false)
                      ?? GameDatabase.Instance?.GetTexture("KSPClubPlugin/icon_fuel", false);
            if (tex != null) return tex;
            // Fallback
            const int size = 38;
            tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var orange = new Color(1f, 0.55f, 0.10f, 1f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, (x < 2 || x >= size-2 || y < 2 || y >= size-2) ? Color.white : orange);
            tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------------ parse helpers

        static float ParsePct(string s, float def)
        {
            if (float.TryParse(s,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float v))
                return Mathf.Clamp01(v / 100f);
            return def;
        }

        static float ParseFloat(string s, float def)
        {
            return float.TryParse(s,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float v) ? v : def;
        }
    }
}
