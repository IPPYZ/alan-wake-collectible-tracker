using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AlanWakeTracker
{
    public class SaveProfile
    {
        public string GameId;
        public string Platform;
        public string Account;
        public string Path;
        public DateTime Modified;

        public string Title
        {
            get
            {
                if (!string.IsNullOrEmpty(Account)) return Account;
                return Path;
            }
        }
    }

    public static class Scanner
    {
        public static List<SaveProfile> Scan()
        {
            List<SaveProfile> list = new List<SaveProfile>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ---- Steam ----
            List<string> roots = SteamRoots();
            foreach (string root in roots)
            {
                try
                {
                    string userdata = Path.Combine(root, "userdata");
                    if (!Directory.Exists(userdata)) continue;
                    Dictionary<string, string> names = LoginUsers(Path.Combine(root, "config", "loginusers.vdf"));
                    foreach (string userDir in Directory.GetDirectories(userdata))
                    {
                        string accountId = Path.GetFileName(userDir);
                        string persona;
                        if (!names.TryGetValue(accountId, out persona)) persona = "Steam " + accountId;
                        foreach (GameDef g in Repo.Games)
                        {
                            string cfg = Path.Combine(userDir, g.SteamApp, "remote", "config");
                            Add(list, seen, g.Id, "Steam", persona, cfg);
                        }
                    }
                }
                catch { }
            }

            // ---- Documents / Remedy folders for every launcher + every user ----
            foreach (string docs in DocumentFolders())
            {
                try
                {
                    string remedy = Path.Combine(docs, "Remedy");
                    if (!Directory.Exists(remedy)) continue;
                    foreach (GameDef g in Repo.Games)
                    {
                        foreach (string folder in g.DocsFolders)
                        {
                            string cfg = Path.Combine(remedy, folder, "config");
                            if (!File.Exists(cfg)) continue;
                            string platform = SaveIO.DetectPlatform(cfg);
                            Add(list, seen, g.Id, platform, Environment.UserName, cfg);
                        }
                    }
                }
                catch { }
            }

            // ---- Deep scan: other drives / custom folders ----
            foreach (string drive in FixedDrives())
            {
                try
                {
                    foreach (string dir in FindNamedDirs(drive, 5, new string[] { "Remedy" }))
                    {
                        foreach (GameDef g in Repo.Games)
                        {
                            foreach (string folder in g.DocsFolders)
                            {
                                string cfg = Path.Combine(dir, folder, "config");
                                if (!File.Exists(cfg)) continue;
                                Add(list, seen, g.Id, SaveIO.DetectPlatform(cfg), Environment.UserName, cfg);
                            }
                        }
                    }
                }
                catch { }
            }

            // ---- Signature search: any file named "config" that really is an Alan Wake / AN save ----
            foreach (string root in SignatureRoots())
            {
                try
                {
                    foreach (string file in FindSaveFiles(root, 5))
                    {
                        string game = DetectSaveSignature(file);
                        if (game == null) continue;
                        Add(list, seen, game, SaveIO.DetectPlatform(file), Environment.UserName, file);
                    }
                }
                catch { }
            }

            list.Sort(delegate(SaveProfile a, SaveProfile b) { return b.Modified.CompareTo(a.Modified); });
            return list;
        }

        static List<string> SignatureRoots()
        {
            List<string> roots = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string> add = delegate(string p)
            {
                if (string.IsNullOrEmpty(p)) return;
                try { if (Directory.Exists(p)) { string full = Path.GetFullPath(p); if (seen.Add(full)) roots.Add(full); } }
                catch { }
            };
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            add(Path.Combine(home, "Saved Games"));
            add(Path.Combine(home, "AppData", "Local"));
            add(Path.Combine(home, "AppData", "Roaming"));
            add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            add(home);
            foreach (string drive in FixedDrives()) add(drive);
            return roots;
        }

        static List<string> FindSaveFiles(string root, int maxDepth)
        {
            List<string> found = new List<string>();
            Queue<KeyValuePair<string, int>> queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(root, 0));
            int guard = 0;
            while (queue.Count > 0 && guard++ < 60000)
            {
                KeyValuePair<string, int> cur = queue.Dequeue();
                string[] files = null;
                try { files = Directory.GetFiles(cur.Key); } catch { files = null; }
                if (files != null)
                {
                    foreach (string f in files)
                    {
                        string name = Path.GetFileName(f);
                        if (!name.Equals("config", StringComparison.OrdinalIgnoreCase) &&
                            !name.Equals("config.bin", StringComparison.OrdinalIgnoreCase)) continue;
                        try
                        {
                            long len = new FileInfo(f).Length;
                            if (len >= 60 && len <= 8192) found.Add(f);
                        }
                        catch { }
                    }
                }
                if (cur.Value + 1 >= maxDepth) continue;
                string[] subs = null;
                try { subs = Directory.GetDirectories(cur.Key); } catch { subs = null; }
                if (subs == null) continue;
                foreach (string sub in subs)
                {
                    string nm = Path.GetFileName(sub);
                    if (nm.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                        nm.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
                        nm.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                        nm.Equals("WinSxS", StringComparison.OrdinalIgnoreCase) ||
                        nm.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                        nm.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                        nm.Equals("Steam", StringComparison.OrdinalIgnoreCase)) continue;
                    queue.Enqueue(new KeyValuePair<string, int>(sub, cur.Value + 1));
                }
            }
            return found;
        }

        static string DetectSaveSignature(string file)
        {
            try
            {
                string lower = file.ToLowerInvariant();
                if (lower.Contains("\\appdata\\local\\temp\\") || lower.Contains("\\backups\\")) return null;
                byte[] b = SaveIO.ReadShared(file);
                if (b.Length < 60) return null;
                string latin = Encoding.GetEncoding(28591).GetString(b);
                if (latin.IndexOf("CANPYRAMIDS", StringComparison.Ordinal) >= 0) return "aw";
                byte[] anchor = new byte[] { 0x3F, 0x00, 0x00, 0x80, 0x3F, 0x04, 0x00, 0x00, 0x00 };
                for (int i = 0; i + anchor.Length <= b.Length; i++)
                {
                    bool ok = true;
                    for (int k = 0; k < anchor.Length; k++) if (b[i + k] != anchor[k]) { ok = false; break; }
                    if (ok) return "an";
                }
                if (b.Length >= 4 && b[0] == 0x96 && b[1] == 0 && b[2] == 0 && b[3] == 0 &&
                    latin.IndexOf("default", StringComparison.Ordinal) >= 0 && b.Length < 1200) return "an";
            }
            catch { }
            return null;
        }

        static void Add(List<SaveProfile> list, HashSet<string> seen, string game, string platform, string account, string path)
        {
            if (!File.Exists(path)) return;
            string full;
            try { full = Path.GetFullPath(path); } catch { return; }
            if (!seen.Add(full)) return;
            SaveProfile p = new SaveProfile();
            p.GameId = game;
            p.Platform = platform;
            p.Account = account;
            p.Path = full;
            try { p.Modified = File.GetLastWriteTime(full); } catch { p.Modified = DateTime.MinValue; }
            list.Add(p);
        }

        public static List<string> FixedDrives()
        {
            List<string> drives = new List<string>();
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.DriveType == DriveType.Fixed && d.IsReady) drives.Add(d.RootDirectory.FullName);
                }
            }
            catch { }
            return drives;
        }

        public static List<string> SteamRoots()
        {
            List<string> roots = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string> add = delegate(string p)
            {
                if (string.IsNullOrEmpty(p)) return;
                try
                {
                    if (!Directory.Exists(p)) return;
                    string full = Path.GetFullPath(p);
                    if (seen.Add(full)) roots.Add(full);
                }
                catch { }
            };
            try { add((string)Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null)); } catch { }
            try { add((string)Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null)); } catch { }
            try { add((string)Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null)); } catch { }
            add(@"C:\Program Files (x86)\Steam");
            add(@"C:\Program Files\Steam");
            add(@"C:\Steam");

            foreach (string drive in FixedDrives())
            {
                add(Path.Combine(drive, "Steam"));
                add(Path.Combine(drive, "SteamLibrary"));
                add(Path.Combine(drive, "Games", "Steam"));
                add(Path.Combine(drive, "Program Files (x86)", "Steam"));
                try
                {
                    foreach (string dir in FindNamedDirs(drive, 5, new string[] { "Steam" }))
                        if (Directory.Exists(Path.Combine(dir, "userdata"))) add(dir);
                }
                catch { }
            }
            return roots;
        }

        public static List<string> DocumentFolders()
        {
            List<string> docs = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string> add = delegate(string p)
            {
                if (string.IsNullOrEmpty(p)) return;
                try { if (Directory.Exists(p)) { string full = Path.GetFullPath(p); if (seen.Add(full)) docs.Add(full); } }
                catch { }
            };
            add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            add(Path.Combine(home, "OneDrive", "Documents"));
            try
            {
                foreach (string od in Directory.GetDirectories(home, "OneDrive*"))
                    add(Path.Combine(od, "Documents"));
            }
            catch { }
            try
            {
                foreach (string user in Directory.GetDirectories(Path.Combine(home, "..")))
                {
                    add(Path.Combine(user, "Documents"));
                    add(Path.Combine(user, "OneDrive", "Documents"));
                }
            }
            catch { }
            foreach (string drive in FixedDrives())
                add(Path.Combine(drive, "Documents"));
            return docs;
        }

        static Dictionary<string, string> LoginUsers(string vdf)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(vdf)) return map;
                string text = File.ReadAllText(vdf);
                MatchCollection ids = Regex.Matches(text, "\"(7656119[0-9]{10})\"");
                MatchCollection personas = Regex.Matches(text, "\"PersonaName\"\\s+\"([^\"]*)\"");
                for (int i = 0; i < ids.Count && i < personas.Count; i++)
                {
                    long id64;
                    if (!long.TryParse(ids[i].Groups[1].Value, out id64)) continue;
                    long accountId = id64 - 76561197960265728L;
                    map[accountId.ToString()] = personas[i].Groups[1].Value;
                }
            }
            catch { }
            return map;
        }

        static List<string> FindNamedDirs(string root, int maxDepth, string[] names)
        {
            List<string> found = new List<string>();
            Queue<KeyValuePair<string, int>> queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(root, 0));
            int guard = 0;
            while (queue.Count > 0 && guard++ < 40000)
            {
                KeyValuePair<string, int> cur = queue.Dequeue();
                string[] subs;
                try { subs = Directory.GetDirectories(cur.Key); }
                catch { continue; }
                foreach (string sub in subs)
                {
                    string name = Path.GetFileName(sub);
                    if (name.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("WinSxS", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (string want in names)
                    {
                        if (name.Equals(want, StringComparison.OrdinalIgnoreCase) ||
                            (want.EndsWith("*") && name.StartsWith(want.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)))
                        {
                            found.Add(sub);
                            break;
                        }
                    }
                    if (cur.Value + 1 < maxDepth) queue.Enqueue(new KeyValuePair<string, int>(sub, cur.Value + 1));
                }
            }
            return found;
        }
    }
}
