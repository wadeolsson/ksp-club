using System.Collections.Generic;

namespace KSPClub
{
    /// <summary>
    /// Configuration for a vessel acting as a fuel tanker.
    /// Persisted in KSPClubScenario (own vessels) and stamped onto vessel
    /// ConfigNodes so other players can see prices in the dynamic layer.
    /// </summary>
    public class TankerConfig
    {
        public bool   Active           = false;
        public float  ReservePct       = 0.20f;  // never sell below this fraction of max
        public float  FriendlyDiscount = 0.00f;  // fraction off for Friendly agencies (0–1)

        // Price per unit for each resource (0 = not for sale)
        public readonly Dictionary<string, float> Prices = new Dictionary<string, float>
        {
            ["LiquidFuel"]     = 10f,
            ["Oxidizer"]       =  8f,
            ["MonoPropellant"] = 25f,
        };

        public string OwnerPlayerId = "";
        public string OwnerAgency   = "";

        // ------------------------------------------------------------------ persistence

        public void Save(ConfigNode node)
        {
            node.AddValue("active",           Active.ToString());
            node.AddValue("reservePct",       ReservePct.ToString(INV));
            node.AddValue("friendlyDiscount", FriendlyDiscount.ToString(INV));
            node.AddValue("ownerPlayerId",    OwnerPlayerId);
            node.AddValue("ownerAgency",      OwnerAgency);
            foreach (var kv in Prices)
                node.AddValue($"price_{kv.Key}", kv.Value.ToString(INV));
        }

        public static TankerConfig Load(ConfigNode node)
        {
            var c = new TankerConfig();
            bool.TryParse (node.GetValue("active")           ?? "false", out c.Active);
            TryFloat(node, "reservePct",       ref c.ReservePct);
            TryFloat(node, "friendlyDiscount", ref c.FriendlyDiscount);
            c.OwnerPlayerId = node.GetValue("ownerPlayerId") ?? "";
            c.OwnerAgency   = node.GetValue("ownerAgency")   ?? "";
            foreach (var key in c.Prices.Keys)
            {
                float v = 0;
                if (TryFloat(node, $"price_{key}", ref v)) c.Prices[key] = v;
            }
            return c;
        }

        static bool TryFloat(ConfigNode n, string key, ref float val)
        {
            string? s = n.GetValue(key);
            if (s == null) return false;
            return float.TryParse(s,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out val);
        }

        static readonly System.Globalization.CultureInfo INV =
            System.Globalization.CultureInfo.InvariantCulture;
    }

    /// <summary>Single fuel-purchase transaction recorded during gameplay.</summary>
    public class TransactionRecord
    {
        public string Buyer               = "";
        public string Seller              = "";
        public string Resource            = "";
        public float  Amount              = 0;
        public float  TotalCost           = 0;
        public uint   TankerPersistentId  = 0;
        public double Timestamp           = 0;

        public void Save(ConfigNode node)
        {
            node.AddValue("buyer",     Buyer);
            node.AddValue("seller",    Seller);
            node.AddValue("resource",  Resource);
            node.AddValue("amount",    Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.AddValue("totalCost", TotalCost.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.AddValue("tankerPid", TankerPersistentId.ToString());
            node.AddValue("timestamp", Timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public static TransactionRecord Load(ConfigNode node)
        {
            var t = new TransactionRecord();
            t.Buyer    = node.GetValue("buyer")    ?? "";
            t.Seller   = node.GetValue("seller")   ?? "";
            t.Resource = node.GetValue("resource") ?? "";
            float.TryParse(node.GetValue("amount")    ?? "0",
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out t.Amount);
            float.TryParse(node.GetValue("totalCost") ?? "0",
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out t.TotalCost);
            uint.TryParse (node.GetValue("tankerPid") ?? "0", out t.TankerPersistentId);
            double.TryParse(node.GetValue("timestamp") ?? "0",
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out t.Timestamp);
            return t;
        }
    }
}
