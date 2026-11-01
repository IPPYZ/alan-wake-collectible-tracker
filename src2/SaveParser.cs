using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AlanWakeTracker
{
    public static class SaveIO
    {
        public static bool Load(string path)
        {
            try
            {
                byte[] b = ReadShared(path);
                string text = Encoding.GetEncoding(28591).GetString(b);
                string game = DetectGame(path, text, b.Length);

                Repo.Platform = DetectPlatform(path);
                if (game == "an")
                {
                    Repo.SetGame("an");
                    Repo.ANPages = ParseANPages(b).ToArray();
                    Repo.Found = new Dictionary<string, List<int>>();
                    Repo.Pages = new List<int>();
                    Repo.PagesNm = new List<int>();
                }
                else
                {
                    Repo.SetGame("aw");
                    List<int> pages, pagesNm;
                    Dictionary<string, List<int>> raw = ParseAW(path, out pages, out pagesNm);
                    Dictionary<string, List<int>> map = new Dictionary<string, List<int>>();
                    map["thermos"] = Pick(raw, "THERMOS");
                    map["chest"] = Pick(raw, "CHESTS");
                    map["cans"] = Pick(raw, "CANPYRAMIDS");
                    map["radio"] = Pick(raw, "RADIOSHOWS");
                    map["tv"] = Pick(raw, "TVSHOWS");
                    map["sign"] = Pick(raw, "SIGNS");
                    map["alarm"] = Pick(raw, "DLC1_ALARMCLOCKS");
                    map["cutout"] = Pick(raw, "DLC1_CARDBOARDCUTOUTS");
                    map["game"] = Pick(raw, "DLC2_NIGHTSPRINGSVIDEOGAMES");
                    Repo.Found = map;
                    Repo.Pages = pages;
                    Repo.PagesNm = pagesNm;
                    Repo.ANPages = new int[0];
                }

                Repo.ConfigPath = path;
                Repo.ConfigStamp = File.GetLastWriteTime(path);
                Repo.LoadError = null;
                return true;
            }
            catch (Exception ex)
            {
                Repo.LoadError = ex.Message;
                return false;
            }
        }

        public static byte[] ReadShared(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] data = new byte[fs.Length];
                int off = 0;
                while (off < data.Length)
                {
                    int n = fs.Read(data, off, data.Length - off);
                    if (n <= 0) break;
                    off += n;
                }
                if (off == data.Length) return data;
                byte[] cut = new byte[off];
                Array.Copy(data, cut, off);
                return cut;
            }
        }

        public static string DetectGame(string path, string text, int size)
        {
            string p = path.ToLowerInvariant();
            if (p.Contains("\\202750\\")) return "an";
            if (p.Contains("\\108710\\")) return "aw";
            if (p.Contains("americannightmare")) return "an";
            if (p.Contains("alanwake") && !p.Contains("remaster")) return "aw";
            if (text.IndexOf("CANPYRAMIDS", StringComparison.Ordinal) >= 0) return "aw";
            if (size < 4096 && text.IndexOf("default", StringComparison.Ordinal) >= 0) return "an";
            return "aw";
        }

        public static string DetectPlatform(string path)
        {
            string p = path.ToLowerInvariant();
            if (p.Contains("\\userdata\\")) return "Steam";
            if (p.Contains("_egs_version")) return "Epic";
            if (p.Contains("_gog_version")) return "GOG";
            if (p.Contains("_ms_version")) return "Microsoft";
            if (p.Contains("\\documents\\remedy")) return "Windows";
            return "Local";
        }

        static List<int> Pick(Dictionary<string, List<int>> m, string key)
        {
            List<int> v;
            if (m.TryGetValue(key, out v)) return v;
            return new List<int>();
        }

        static readonly byte[] ANAnchor = new byte[] { 0x3F, 0x00, 0x00, 0x80, 0x3F, 0x04, 0x00, 0x00, 0x00 };

        static int LastIndexOf(byte[] b, byte[] pat)
        {
            for (int i = b.Length - pat.Length; i >= 0; i--)
            {
                bool ok = true;
                for (int k = 0; k < pat.Length; k++) if (b[i + k] != pat[k]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        public static List<int> ParseANPages(byte[] b)
        {
            // Primary layout: the manuscript list follows the fixed marker block.
            int anchor = LastIndexOf(b, ANAnchor);
            if (anchor >= 0)
            {
                int start = anchor + ANAnchor.Length;
                int limit = Math.Min(b.Length, start + 30);
                for (int i = start; i < limit; i++)
                {
                    int n = b[i];
                    if (n < 1 || n > 53) continue;
                    if (i + 1 + n > b.Length) continue;
                    List<int> ids = new List<int>();
                    if (ValidANList(b, i + 1, n, ids)) return ids;
                }
            }
            int bestScore = -1, bestOff = -1;
            List<int> best = null;
            for (int i = 0; i + 1 < b.Length; i++)
            {
                int n1 = b[i];
                if (n1 < 2 || n1 > 53) continue;
                if (i + 1 + n1 >= b.Length) continue;
                List<int> ids1 = new List<int>();
                if (!ValidANList(b, i + 1, n1, ids1)) continue;
                int j = i + 1 + n1;
                int n2 = b[j];
                if (n2 != n1) continue;
                if (j + 1 + n2 > b.Length) continue;
                List<int> ids2 = new List<int>();
                if (!ValidANList(b, j + 1, n2, ids2)) continue;
                bool same = true;
                for (int q = 0; q < n1; q++) if (ids1[q] != ids2[q]) { same = false; break; }
                if (!same) continue;
                int score = n1 * 100;
                if (score > bestScore || (score == bestScore && i > bestOff))
                {
                    bestScore = score;
                    bestOff = i;
                    best = ids1;
                }
            }
            return best == null ? new List<int>() : best;
        }

        static bool ValidANList(byte[] b, int start, int n, List<int> ids)
        {
            if (n < 1 || start + n > b.Length) return false;
            for (int k = 0; k < n; k++)
            {
                int v = b[start + k];
                if (v > 52) return false;
                if (k > 0 && v <= ids[k - 1]) return false;
                ids.Add(v);
            }
            return ids[0] == 0;
        }

        public static Dictionary<string, List<int>> ParseAW(string path, out List<int> pages, out List<int> pagesNm)
        {
            byte[] b = ReadShared(path);
            string text = Encoding.GetEncoding(28591).GetString(b);
            int at = text.IndexOf("CANPYRAMIDS", StringComparison.Ordinal);
            if (at < 6) throw new Exception("Not an Alan Wake save");
            int tableStart = at - 5;
            int n = b[tableStart];
            int pos = tableStart + 1;
            Dictionary<string, List<int>> map = new Dictionary<string, List<int>>();
            for (int i = 0; i < n; i++)
            {
                uint len = BitConverter.ToUInt32(b, pos); pos += 4;
                string name = Encoding.ASCII.GetString(b, pos, (int)len); pos += (int)len;
                int cnt = b[pos]; pos++;
                List<int> ids = new List<int>();
                for (int k = 0; k < cnt; k++) ids.Add(b[pos + k]);
                pos += cnt;
                map[name] = ids;
            }

            pages = new List<int>();
            pagesNm = new List<int>();
            int bestScore = -1;
            for (int st = 0; st < tableStart; st++)
            {
                int nF = b[st];
                if (nF == 0 || nF > 120) continue;
                int p2 = st + 1 + nF;
                if (p2 >= tableStart) continue;
                int nR = b[p2];
                if (nR > 120) continue;
                if (p2 + 1 + nR != tableStart) continue;
                List<int> ids = new List<int>();
                bool asc = true;
                for (int k = 0; k < nF; k++)
                {
                    ids.Add(b[st + 1 + k]);
                    if (k > 0 && b[st + 1 + k] < b[st + k]) asc = false;
                }
                int score = nF * 10 + (asc ? 5 : 0) + (ids.Count > 0 && ids[0] == 0 ? 5 : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    pages = ids;
                    pagesNm = new List<int>();
                    for (int k = 0; k < nR; k++) pagesNm.Add(b[p2 + 1 + k]);
                }
            }
            return map;
        }

        public static string Summary(string path, string gameId)
        {
            try
            {
                GameDef g = Repo.Game(gameId);
                if (g == null) return "";
                if (gameId == "an")
                {
                    List<int> ids = ParseANPages(File.ReadAllBytes(path));
                    return ids.Count + " / " + g.Items.Count;
                }
                List<int> pages, pagesNm;
                Dictionary<string, List<int>> map = ParseAW(path, out pages, out pagesNm);
                int have = pages.Count + pagesNm.Count;
                foreach (ItemData it in g.Items)
                {
                    if (it.cat == "page" || it.cat == "nm") continue;
                    List<int> list;
                    if (!map.TryGetValue(SectionKey(it.cat), out list)) continue;
                    if (it.cat == "thermos")
                    {
                        int internalId = it.id < 90 ? it.id : it.id + 1;
                        if (list.Contains(internalId)) have++;
                    }
                    else if (list.Contains(it.id)) have++;
                }
                return have + " / " + g.Items.Count;
            }
            catch { return "?"; }
        }

        static string SectionKey(string cat)
        {
            switch (cat)
            {
                case "thermos": return "THERMOS";
                case "chest": return "CHESTS";
                case "cans": return "CANPYRAMIDS";
                case "radio": return "RADIOSHOWS";
                case "tv": return "TVSHOWS";
                case "sign": return "SIGNS";
                case "alarm": return "DLC1_ALARMCLOCKS";
                case "cutout": return "DLC1_CARDBOARDCUTOUTS";
                case "game": return "DLC2_NIGHTSPRINGSVIDEOGAMES";
                default: return "";
            }
        }

        public static string Backup(string cfg)
        {
            if (string.IsNullOrEmpty(cfg) || !File.Exists(cfg)) throw new Exception("Save file not found");
            string dir = Path.Combine(AppDir(), "Backups");
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, "config_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak");
            File.Copy(cfg, dest, false);
            return dest;
        }

        static readonly HashSet<string> backedUp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static string BackupNow(string cfg, string tag)
        {
            if (string.IsNullOrEmpty(cfg) || !File.Exists(cfg)) return null;
            string dir = Path.Combine(AppDir(), "Backups");
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, Path.GetFileName(cfg) + "_" + tag + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak");
            File.Copy(cfg, dest, false);
            // keep the newest 25 backups of the same tag
            try
            {
                FileInfo[] files = new DirectoryInfo(dir).GetFiles(Path.GetFileName(cfg) + "_" + tag + "_*.bak");
                Array.Sort(files, delegate(FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
                for (int i = 25; i < files.Length; i++) files[i].Delete();
            }
            catch { }
            return dest;
        }

        public static string BackupOncePerSession(string cfg)
        {
            if (string.IsNullOrEmpty(cfg)) return null;
            string full;
            try { full = Path.GetFullPath(cfg); } catch { return null; }
            lock (backedUp)
            {
                if (backedUp.Contains(full)) return null;
                backedUp.Add(full);
            }
            try { return BackupNow(cfg, "auto"); } catch { return null; }
        }

        public static void AutoBackup(string cfg)
        {
            try
            {
                if (string.IsNullOrEmpty(cfg) || !File.Exists(cfg)) return;
                string dir = Path.Combine(AppDir(), "Backups");
                Directory.CreateDirectory(dir);
                string dest = Path.Combine(dir, "config_" + DateTime.Now.ToString("yyyyMMdd") + ".bak");
                if (!File.Exists(dest)) File.Copy(cfg, dest);
            }
            catch { }
        }

        public static string AppDir()
        {
            return Path.GetDirectoryName(Application.ExecutablePath);
        }
    }

    public static class Settings
    {
        public static string Lang = "en";
        public static double WinWidth = 1220;
        public static double WinHeight = 820;
        public static double WinLeft = double.NaN;
        public static double WinTop = double.NaN;
        public static string Mode = "missing";
        public static string LastGame = "";
        public static string LastProfile = "";
        public static string SortBy = "ep";
        public static Dictionary<string, bool> Manual = new Dictionary<string, bool>();

        static string FilePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlanWakeTracker", "settings.json");
        }

        public static void Load()
        {
            try
            {
                string f = FilePath();
                if (!File.Exists(f)) return;
                JavaScriptSerializer ser = new JavaScriptSerializer();
                Dictionary<string, object> m = (Dictionary<string, object>)ser.DeserializeObject(File.ReadAllText(f, Encoding.UTF8));
                object v;
                if (m.TryGetValue("lang", out v) && v != null) Lang = v.ToString();
                if (m.TryGetValue("mode", out v) && v != null) Mode = v.ToString();
                if (m.TryGetValue("lastGame", out v) && v != null) LastGame = v.ToString();
                if (m.TryGetValue("lastProfile", out v) && v != null) LastProfile = v.ToString();
                if (m.TryGetValue("sortBy", out v) && v != null) SortBy = v.ToString();
                object man;
                if (m.TryGetValue("manual", out man) && man is Dictionary<string, object>)
                {
                    Manual.Clear();
                    foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)man)
                    {
                        bool b = false;
                        if (kv.Value is bool) b = (bool)kv.Value;
                        else if (kv.Value != null) bool.TryParse(kv.Value.ToString(), out b);
                        Manual[kv.Key] = b;
                    }
                }
                if (m.TryGetValue("w", out v) && v != null) { double d; if (double.TryParse(v.ToString(), out d) && d > 800) WinWidth = d; }
                if (m.TryGetValue("h", out v) && v != null) { double d; if (double.TryParse(v.ToString(), out d) && d > 500) WinHeight = d; }
                if (m.TryGetValue("x", out v) && v != null) { double d; if (double.TryParse(v.ToString(), out d)) WinLeft = d; }
                if (m.TryGetValue("y", out v) && v != null) { double d; if (double.TryParse(v.ToString(), out d)) WinTop = d; }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                string f = FilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                Dictionary<string, object> m = new Dictionary<string, object>();
                m["lang"] = Lang;
                m["mode"] = Mode;
                m["lastGame"] = LastGame;
                m["lastProfile"] = LastProfile;
                m["sortBy"] = SortBy;
                m["manual"] = Manual;
                m["w"] = WinWidth;
                m["h"] = WinHeight;
                File.WriteAllText(f, new JavaScriptSerializer().Serialize(m), Encoding.UTF8);
            }
            catch { }
        }
    }
}
