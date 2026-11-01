using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

namespace AlanWakeTracker
{
    public class ItemData
    {
        public string cat;
        public int id;
        public int ep;
        public string sk;
        public string n;
        public Dictionary<string, string> d;

        public string Loc(string lang)
        {
            if (d == null) return "";
            string v;
            if (d.TryGetValue(lang, out v) && !string.IsNullOrEmpty(v)) return v;
            if (d.TryGetValue("en", out v) && !string.IsNullOrEmpty(v)) return v;
            return "";
        }
    }

    public class CategoryInfo
    {
        public string Key;
        public string Icon;
        public int Order;
        public CategoryInfo(string key, string icon, int order) { Key = key; Icon = icon; Order = order; }
    }

    public class GameDef
    {
        public string Id;
        public string TitleKey;
        public string DescKey;
        public string SteamApp;
        public string[] DocsFolders;
        public string[] Categories;
        public string[] Icons;
        public List<ItemData> Items = new List<ItemData>();
    }

    public static class Repo
    {
        public static List<GameDef> Games = new List<GameDef>();
        public static GameDef CurrentGame;
        public static List<CategoryInfo> Categories = new List<CategoryInfo>();

        public static Dictionary<string, Dictionary<string, string>> Strings = new Dictionary<string, Dictionary<string, string>>();
        public static Dictionary<string, string> LangNames = new Dictionary<string, string>();
        public static List<string> Langs = new List<string> { "en", "ar", "tr", "es", "zh", "ru", "de", "fr" };
        public static string Lang = "en";

        public static Dictionary<string, List<int>> Found = new Dictionary<string, List<int>>();
        public static List<int> Pages = new List<int>();
        public static List<int> PagesNm = new List<int>();
        public static int[] ANPages = new int[0];
        public static string ConfigPath = null;
        public static DateTime ConfigStamp = DateTime.MinValue;
        public static string LoadError = null;
        public static string Platform = "";
        public static bool ManualMode = false;

        public static void Load()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;

            LoadStrings(asm, ser);

            GameDef aw = new GameDef();
            aw.Id = "aw";
            aw.TitleKey = "game.aw";
            aw.DescKey = "game.aw.desc";
            aw.SteamApp = "108710";
            aw.DocsFolders = new string[] { "AlanWake", "AlanWake_EGS_Version", "AlanWake_GOG_Version", "AlanWake_MS_Version" };
            aw.Categories = new string[] { "page", "nm", "thermos", "chest", "cans", "radio", "tv", "sign", "alarm", "cutout", "game" };
            aw.Icons = new string[] { "📄", "🌙", "☕", "📦", "🥫", "📻", "📺", "🪧", "⏰", "🧍", "🎮" };
            aw.Items = ParseItems(ReadResource(asm, "data.json"), ser);
            Games.Add(aw);

            GameDef an = new GameDef();
            an.Id = "an";
            an.TitleKey = "game.an";
            an.DescKey = "game.an.desc";
            an.SteamApp = "202750";
            an.DocsFolders = new string[] { "AmericanNightmare", "AmericanNightmare_EGS_Version", "AmericanNightmare_GOG_Version", "AmericanNightmare_MS_Version" };
            an.Categories = new string[] { "page" };
            an.Icons = new string[] { "📄" };
            an.Items = ParseItems(ReadResource(asm, "an.json"), ser);
            Games.Add(an);

            CurrentGame = aw;
            RebuildCategories();
        }

        static void LoadStrings(Assembly asm, JavaScriptSerializer ser)
        {
            Dictionary<string, object> root = (Dictionary<string, object>)ser.DeserializeObject(ReadResource(asm, "i18n.json"));
            object names;
            if (root.TryGetValue("names", out names) && names is Dictionary<string, object>)
                foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)names)
                    LangNames[kv.Key] = kv.Value.ToString();
            object strings;
            if (root.TryGetValue("strings", out strings) && strings is Dictionary<string, object>)
            {
                foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)strings)
                {
                    Dictionary<string, string> dict = new Dictionary<string, string>();
                    Dictionary<string, object> src = kv.Value as Dictionary<string, object>;
                    if (src != null)
                        foreach (KeyValuePair<string, object> kv2 in src)
                            dict[kv2.Key] = kv2.Value == null ? "" : kv2.Value.ToString();
                    Strings[kv.Key] = dict;
                }
            }
        }

        static List<ItemData> ParseItems(string json, JavaScriptSerializer ser)
        {
            List<ItemData> list = new List<ItemData>();
            object[] arr = (object[])ser.DeserializeObject(json);
            for (int i = 0; i < arr.Length; i++)
            {
                Dictionary<string, object> m = (Dictionary<string, object>)arr[i];
                ItemData it = new ItemData();
                it.cat = Str(m, "cat");
                it.id = Int(m, "id");
                it.ep = Int(m, "ep");
                it.sk = Str(m, "sk");
                it.n = Str(m, "n");
                it.d = new Dictionary<string, string>();
                object dd;
                if (m.TryGetValue("d", out dd) && dd is Dictionary<string, object>)
                    foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)dd)
                        it.d[kv.Key] = kv.Value == null ? "" : kv.Value.ToString();
                list.Add(it);
            }
            return list;
        }

        public static void SetGame(string id)
        {
            for (int i = 0; i < Games.Count; i++) if (Games[i].Id == id) CurrentGame = Games[i];
            RebuildCategories();
        }

        static void RebuildCategories()
        {
            Categories.Clear();
            if (CurrentGame == null) return;
            for (int i = 0; i < CurrentGame.Categories.Length; i++)
                Categories.Add(new CategoryInfo(CurrentGame.Categories[i], CurrentGame.Icons[i], i));
        }

        public static List<ItemData> Items { get { return CurrentGame.Items; } }

        static string ReadResource(Assembly asm, string suffix)
        {
            foreach (string name in asm.GetManifestResourceNames())
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    using (Stream s = asm.GetManifestResourceStream(name))
                    using (StreamReader r = new StreamReader(s, System.Text.Encoding.UTF8))
                        return r.ReadToEnd();
                }
            throw new Exception("Missing resource: " + suffix);
        }

        static string Str(Dictionary<string, object> m, string k)
        {
            object v;
            if (m.TryGetValue(k, out v) && v != null) return v.ToString();
            return "";
        }

        static int Int(Dictionary<string, object> m, string k)
        {
            object v;
            if (m.TryGetValue(k, out v) && v != null)
            {
                int i;
                if (int.TryParse(v.ToString(), out i)) return i;
            }
            return 0;
        }

        public static string T(string key) { return T(key, null); }

        public static string T(string key, params object[] args)
        {
            Dictionary<string, string> table;
            string text = null;
            if (Strings.TryGetValue(Lang, out table)) table.TryGetValue(key, out text);
            if (text == null)
                if (Strings.TryGetValue("en", out table)) table.TryGetValue(key, out text);
            if (text == null) text = key;
            if (args != null && args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                    text = text.Replace("{" + i + "}", args[i] == null ? "" : args[i].ToString());
                text = text.Replace("{n}", args[0] == null ? "" : args[0].ToString());
            }
            return text;
        }

        public static string CatName(string cat) { return T("cat." + cat); }

        public static string EpName(int ep)
        {
            if (CurrentGame != null && CurrentGame.Id == "an") return T("an.act" + ep);
            return T("ep." + ep);
        }

        public static string SecName(ItemData it)
        {
            if (CurrentGame != null && CurrentGame.Id == "an") return T("an.area_" + it.sk);
            return T("sec." + it.sk);
        }

        public static string ManualKey(ItemData it)
        {
            return (ConfigPath == null ? "" : ConfigPath) + "|" + CurrentGame.Id + "|" + it.cat + "|" + it.id;
        }

        public static bool IsCollected(ItemData it)
        {

            if (CurrentGame != null && CurrentGame.Id == "an") return Array.IndexOf(ANPages, it.id - 1) >= 0;
            if (it.cat == "page") return Pages.Contains(it.id - 1);
            if (it.cat == "nm") return PagesNm.Contains(it.id - 1);
            List<int> list;
            if (!Found.TryGetValue(it.cat, out list) || list == null) return false;
            if (it.cat == "thermos")
            {
                int internalId = it.id < 90 ? it.id : it.id + 1;
                return list.Contains(internalId);
            }
            return list.Contains(it.id);
        }

        public static int CurrentEpisode()
        {
            try
            {
                if (ConfigPath == null) return -1;
                byte[] b = SaveIO.ReadShared(ConfigPath);
                if (CurrentGame.Id == "aw")
                {
                    if (b.Length < 0x100) return -1;
                    int lvl = b[0xFA];
                    if (lvl >= 1 && lvl <= 3) return 1;
                    if (lvl == 4 || lvl == 5) return 2;
                    if (lvl == 6 || lvl == 7 || lvl == 8 || lvl == 9 || lvl == 10) return 3;
                    if (lvl == 11 || lvl == 12 || lvl == 13) return 4;
                    if (lvl == 14 || lvl == 15) return 5;
                    if (lvl == 16 || lvl == 17) return 6;
                    if (lvl == 100) return 7;
                    if (lvl == 200) return 8;
                    return -1;
                }
                string dir = System.IO.Path.GetDirectoryName(ConfigPath);
                string sg = System.IO.Path.Combine(dir, "savegame.aws");
                if (!System.IO.File.Exists(sg)) return -1;
                string txt = System.Text.Encoding.GetEncoding(28591).GetString(SaveIO.ReadShared(sg)).Substring(0, 4000);
                int idx = txt.IndexOf("gameworld:scene");
                if (idx < 0) return -1;
                string rest = txt.Substring(idx + 15);
                int digits = 0;
                while (digits < rest.Length && char.IsDigit(rest[digits])) digits++;
                if (digits == 0) return -1;
                int scene = int.Parse(rest.Substring(0, digits));
                if (scene <= 2) return 1;
                if (scene == 3) return 2;
                return 3;
            }
            catch { return -1; }
        }

        public static int DisplayNumber(ItemData it)
        {
            // The save (and the official wiki/Prima numbering) is authoritative.
            return it.id;
        }
        public static void ClearManual()
        {
            Settings.Manual.Clear();
        }

        public static int Total(string cat)
        {
            int n = 0;
            for (int i = 0; i < Items.Count; i++) if (Items[i].cat == cat) n++;
            return n;
        }

        public static int Have(string cat)
        {
            int n = 0;
            for (int i = 0; i < Items.Count; i++) if (Items[i].cat == cat && IsCollected(Items[i])) n++;
            return n;
        }

        public static int TotalAll() { return Items.Count; }

        public static int HaveAll()
        {
            int n = 0;
            for (int i = 0; i < Items.Count; i++) if (IsCollected(Items[i])) n++;
            return n;
        }

        public static List<ItemData> ByCat(string cat)
        {
            List<ItemData> list = new List<ItemData>();
            for (int i = 0; i < Items.Count; i++) if (Items[i].cat == cat) list.Add(Items[i]);
            return list;
        }

        public static List<int> EpisodesOf(string cat)
        {
            List<int> eps = new List<int>();
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].cat == cat && !eps.Contains(Items[i].ep)) eps.Add(Items[i].ep);
            eps.Sort();
            return eps;
        }

        public static GameDef Game(string id)
        {
            for (int i = 0; i < Games.Count; i++) if (Games[i].Id == id) return Games[i];
            return null;
        }
    }
}
