using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace AlanWakeTracker
{
    public partial class MainWindow : Window
    {
        static readonly SolidColorBrush CText = new SolidColorBrush(Color.FromRgb(0xEC, 0xED, 0xEF));
        static readonly SolidColorBrush CMuted = new SolidColorBrush(Color.FromRgb(0xA6, 0xAC, 0xB2));
        static readonly SolidColorBrush CDim = new SolidColorBrush(Color.FromRgb(0x70, 0x76, 0x7C));
        static readonly SolidColorBrush CPanel = new SolidColorBrush(Color.FromRgb(0x17, 0x1B, 0x1F));
        static readonly SolidColorBrush CPanelHi = new SolidColorBrush(Color.FromRgb(0x1E, 0x23, 0x28));
        static readonly SolidColorBrush CStroke = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x35));
        static readonly SolidColorBrush CAmber = new SolidColorBrush(Color.FromRgb(0xD9, 0xA4, 0x41));
        static readonly SolidColorBrush CGreen = new SolidColorBrush(Color.FromRgb(0x7D, 0xBA, 0x8E));
        static readonly SolidColorBrush CRed = new SolidColorBrush(Color.FromRgb(0xC9, 0x6F, 0x6B));

        const string BugReportUrl = "https://www.nexusmods.com/alanwake/mods/";
        string currentCat = "overview";
        string mode = "missing";
        int epFilter = -1;
        string sortBy = "ep";
        string search = "";
        TextBox searchBox;
        TextBlock placeholder;
        bool loaded;
        List<SaveProfile> profiles = new List<SaveProfile>();
        bool scanning = false;
        string view = "game";
        string pendingGame = "aw";

        public MainWindow()
        {
            InitializeComponent();
            Settings.Load();
            sortBy = Settings.SortBy == null ? "ep" : Settings.SortBy;
            Repo.ClearManual();
            Settings.Save();
            Repo.Lang = Settings.Lang;
            if (!Repo.Langs.Contains(Repo.Lang)) Repo.Lang = "en";
            mode = Settings.Mode;
            if (mode != "missing" && mode != "all" && mode != "have") mode = "missing";
            if (Settings.WinWidth > 300) Width = Settings.WinWidth;
            if (Settings.WinHeight > 300) Height = Settings.WinHeight;
            if (Width > SystemParameters.WorkArea.Width - 20) Width = SystemParameters.WorkArea.Width - 40;
            if (Height > SystemParameters.WorkArea.Height - 20) Height = SystemParameters.WorkArea.Height - 40;
            if (Width < 1040) Width = Math.Min(1040, SystemParameters.WorkArea.Width - 40);
            if (Height < 660) Height = Math.Min(660, SystemParameters.WorkArea.Height - 40);
            if (!double.IsNaN(Settings.WinLeft) && !double.IsNaN(Settings.WinTop))
            {
                double vx = SystemParameters.VirtualScreenLeft, vy = SystemParameters.VirtualScreenTop;
                double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
                if (Settings.WinLeft >= vx - 40 && Settings.WinTop >= vy - 40 &&
                    Settings.WinLeft < vx + vw - 200 && Settings.WinTop < vy + vh - 200)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = Settings.WinLeft;
                    Top = Settings.WinTop;
                }
            }

            string[] cmd = Environment.GetCommandLineArgs();
            string testSave = null, testGame = null, testView = null, testHover = null;
            for (int ci = 0; ci < cmd.Length; ci++)
            {
                if (cmd[ci] == "--cat" && ci + 1 < cmd.Length) currentCat = cmd[ci + 1];
                if (cmd[ci] == "--lang" && ci + 1 < cmd.Length) Repo.Lang = cmd[ci + 1];
                if (cmd[ci] == "--save" && ci + 1 < cmd.Length) testSave = cmd[ci + 1];
                if (cmd[ci] == "--game" && ci + 1 < cmd.Length) testGame = cmd[ci + 1];
                if (cmd[ci] == "--viewimg" && ci + 1 < cmd.Length) testView = cmd[ci + 1];
                if (cmd[ci] == "--hoverimg" && ci + 1 < cmd.Length) testHover = cmd[ci + 1];
            }
            if (!Repo.Langs.Contains(Repo.Lang)) Repo.Lang = "en";

            BuildLanguageMenu();
            LangBtn.Click += LangBtn_Click;
            SwitchBtn.Click += SwitchBtn_Click;
            ApplyDirection();

            string startGame = Settings.LastGame;
            if (startGame != "aw" && startGame != "an") startGame = "aw";
            Repo.SetGame(testGame == "an" ? "an" : startGame);
            UpdateTitle();
            if (testSave != null && SaveIO.Load(testSave))
            {
                view = "main";
                SetSidebar(true);
                BuildAll();
                StartSaveWatcher(testSave);
            }
            else if (testGame == "an" || testGame == "aw")
            {
                pendingGame = testGame;
                ShowProfileSelect(testGame);
            }
            else
            {
                ShowGameSelect();
            }
            RunScan();
            if (testView != null) Dispatcher.BeginInvoke(new Action(delegate { ShowImageViewer(testView, "Preview"); }));
            if (testHover != null) Dispatcher.BeginInvoke(new Action(delegate { ShowHoverPreviewAt(testHover, "Preview", 260, 420); }));
            if (Array.IndexOf(cmd, "--about") >= 0) Dispatcher.BeginInvoke(new Action(delegate { ShowAbout(); }));

            loaded = true;

            SourceInitialized += delegate { RoundCorners(); };
            Closing += delegate
            {
                Settings.WinWidth = Width;
                Settings.WinHeight = Height;
                Settings.WinLeft = Left;
                Settings.WinTop = Top;
                Settings.Save();
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                LogCrash(e.ExceptionObject as Exception);
            };
            if (Application.Current != null)
                Application.Current.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    LogCrash(e.Exception);
                };
            PreviewKeyDown += OnShortcut;
        }

        /* ---------------- infrastructure ---------------- */

        void BuildLanguageMenu()
        {
            LangMenu.Items.Clear();
            foreach (string lang in Repo.Langs)
            {
                MenuItem mi = new MenuItem();
                string nm;
                if (!Repo.LangNames.TryGetValue(lang, out nm)) nm = lang;
                mi.Header = nm;
                mi.Tag = lang;
                mi.Click += delegate(object s, RoutedEventArgs e)
                {
                    MenuItem m = (MenuItem)s;
                    Repo.Lang = (string)m.Tag;
                    Settings.Lang = Repo.Lang;
                    Settings.Save();
                    ApplyDirection();
                    BuildAll();
                };
                LangMenu.Items.Add(mi);
            }
        }

        void LangBtn_Click(object sender, RoutedEventArgs e)
        {
            if (LangMenu == null) return;
            LangMenu.PlacementTarget = LangBtn;
            LangMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            LangMenu.IsOpen = true;
        }

        void ApplyDirection()
        {
            RootGrid.FlowDirection = Repo.Lang == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            TitleBar.FlowDirection = FlowDirection.LeftToRight; // window buttons stay top-right
        }

        static string Ltr(string s) { return "\u202A" + s + "\u202C"; }

        void UpdateTitle()
        {
            bool an = Repo.CurrentGame != null && Repo.CurrentGame.Id == "an";
            TitleText.Text = an ? "American Nightmare" : "Alan Wake";
            TitleSub.Text = an ? "Steam 202750" : Repo.T("ui.pill");
            string ln;
            if (!Repo.LangNames.TryGetValue(Repo.Lang, out ln)) ln = Repo.Lang.ToUpperInvariant();
            LangBtn.Content = ln;
            SwitchBtn.Content = "⇄";
            SwitchBtn.ToolTip = Repo.T("ui.switch");
            StatusRight.Text = "v2.6.6";
        }

        void BuildAll()
        {
            UpdateTitle();
            if (view != "main") { UpdateChooser(); return; }
            BuildNav();
            if (currentCat == "overview") ShowOverview(); else ShowCategory(currentCat);
            UpdateStatus();
        }

        void UpdateChooser()
        {
            if (view == "game") ShowGameSelect();
            else if (view == "profile") ShowProfileSelect(pendingGame);
        }

        void SetSidebar(bool on)
        {
            SidebarCol.Width = on ? new GridLength(252) : new GridLength(0);
            Sidebar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        void ShowGameSelect()
        {
            StopSaveWatcher();
            view = "game";
            SetSidebar(false);
            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            StackPanel root = new StackPanel();
            sv.Content = root;

            TextBlock title = new TextBlock();
            title.Text = Repo.T("ui.chooseGame");
            title.Style = (Style)FindResource("H1");
            root.Children.Add(title);
            TextBlock sub = new TextBlock();
            sub.Text = Repo.T("ui.chooseGameSub");
            sub.Style = (Style)FindResource("Body");
            sub.Margin = new Thickness(0, 4, 0, 22);
            root.Children.Add(sub);

            Border note = new Border();
            note.Background = new SolidColorBrush(Color.FromRgb(0x17, 0x1B, 0x1F));
            note.BorderBrush = CStroke;
            note.BorderThickness = new Thickness(1);
            note.CornerRadius = new CornerRadius(12);
            note.Padding = new Thickness(16, 12, 16, 12);
            note.Margin = new Thickness(0, 0, 16, 20);
            StackPanel noteText = new StackPanel();
            TextBlock noteTitle = new TextBlock();
            noteTitle.Text = Repo.T("ui.disclaimerTitle");
            noteTitle.FontSize = 13.5;
            noteTitle.FontWeight = FontWeights.SemiBold;
            noteTitle.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x7A, 0x7A));
            TextBlock noteBody = new TextBlock();
            noteBody.Text = "\u26A0\uFE0F  " + Repo.T("ui.disclaimerText");
            noteBody.FontSize = 13;
            noteBody.Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0x6A, 0x6A));
            noteBody.TextWrapping = TextWrapping.Wrap;
            noteBody.Margin = new Thickness(0, 6, 0, 0);
            noteText.Children.Add(noteTitle);
            noteText.Children.Add(noteBody);
            note.Child = noteText;

            root.Children.Add(note);

            UniformGrid grid = new UniformGrid();
            grid.Columns = 2;
            foreach (GameDef g in Repo.Games)
            {
                Border holder = new Border();
                holder.Margin = new Thickness(0, 0, 16, 16);
                holder.Child = GameCard(g, CountProfiles(g.Id));
                grid.Children.Add(holder);
            }
            root.Children.Add(grid);

            Button again = ActionButton(Repo.T("ui.scanAgain"), "GhostBtn");
            again.HorizontalAlignment = HorizontalAlignment.Left;
            again.Margin = new Thickness(0, 2, 0, 10);
            again.Click += delegate { profiles = new List<SaveProfile>(); scanning = true; ShowGameSelect(); RunScan(); };
            root.Children.Add(again);
            if (scanning)
            {
                TextBlock scan = new TextBlock();
                scan.Text = Repo.T("ui.scanning");
                scan.Style = (Style)FindResource("Body");
                scan.Margin = new Thickness(2, 6, 0, 0);
                root.Children.Add(scan);
            }

            SetContent(sv);
            UpdateStatus();
        }

        UIElement GameCard(GameDef g, int count)
        {
            Border card = new Border();
            card.CornerRadius = new CornerRadius(18);
            card.BorderThickness = new Thickness(1);
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x35));
            card.Padding = new Thickness(24, 22, 24, 22);
            card.Cursor = Cursors.Hand;
            LinearGradientBrush bg = new LinearGradientBrush();
            bg.StartPoint = new Point(0, 0);
            bg.EndPoint = new Point(1, 1);
            if (g.Id == "an")
            {
                bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x2A, 0x20, 0x24), 0));
                bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x17, 0x1B, 0x1F), 1));
            }
            else
            {
                bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x1E, 0x23, 0x28), 0));
                bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x15, 0x18, 0x1B), 1));
            }
            card.Background = bg;
            card.MouseEnter += delegate { card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x47)); };
            card.MouseLeave += delegate { card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x35)); };
            card.MouseLeftButtonUp += delegate { pendingGame = g.Id; ShowProfileSelect(g.Id); };

            StackPanel sp = new StackPanel();
            TextBlock icon = new TextBlock();
            icon.Text = g.Id == "an" ? "AN" : "AW";
            icon.FontFamily = new FontFamily("Segoe UI");
            icon.FontSize = 24;
            sp.Children.Add(icon);
            TextBlock name = new TextBlock();
            name.Text = Repo.T(g.TitleKey);
            name.FontSize = 20;
            name.FontWeight = FontWeights.SemiBold;
            name.Foreground = CText;
            name.Margin = new Thickness(0, 12, 0, 6);
            sp.Children.Add(name);
            TextBlock desc = new TextBlock();
            desc.Text = Repo.T(g.DescKey);
            desc.FontSize = 12;
            desc.Foreground = CMuted;
            desc.TextWrapping = TextWrapping.Wrap;
            desc.LineHeight = 18;
            sp.Children.Add(desc);
            TextBlock cnt = new TextBlock();
            cnt.Text = count > 0 ? Repo.T("ui.profilesFound", count) : Repo.T("ui.noProfiles");
            cnt.FontSize = 12;
            cnt.Foreground = count > 0 ? CAmber : CDim;
            cnt.Margin = new Thickness(0, 16, 0, 0);
            sp.Children.Add(cnt);
            card.Child = sp;
            return card;
        }

        int CountProfiles(string gameId)
        {
            int n = 0;
            for (int i = 0; i < profiles.Count; i++) if (profiles[i].GameId == gameId) n++;
            return n;
        }

        void ShowProfileSelect(string gameId)
        {
            StopSaveWatcher();
            view = "profile";
            pendingGame = gameId;
            SetSidebar(false);
            GameDef g = Repo.Game(gameId);
            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            StackPanel root = new StackPanel();
            sv.Content = root;

            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Margin = new Thickness(0, 0, 0, 16);

            Button back = new Button();
            back.Style = (Style)FindResource("GhostBtn");
            back.Content = "‹";
            back.FontSize = 18;
            back.Padding = new Thickness(12, 0, 12, 4);
            back.Height = 38;
            back.Margin = new Thickness(0, 0, 10, 0);
            back.Click += delegate { ShowGameSelect(); };
            head.Children.Add(back);

            StackPanel titles = new StackPanel();
            TextBlock title = new TextBlock();
            title.Text = Repo.T("ui.chooseProfile");
            title.Style = (Style)FindResource("H1");
            title.FontSize = 21;
            TextBlock sub = new TextBlock();
            sub.Text = g == null ? "" : Repo.T(g.TitleKey);
            sub.Style = (Style)FindResource("Body");
            sub.Margin = new Thickness(0, 2, 0, 0);
            titles.Children.Add(title);
            titles.Children.Add(sub);
            Grid.SetColumn(titles, 1);
            head.Children.Add(titles);

            Button browse = ActionButton(Repo.T("ui.chooseFile"), "GhostBtn");
            browse.VerticalAlignment = VerticalAlignment.Center;
            browse.Click += delegate { PromptForSave(); };
            Grid.SetColumn(browse, 2);
            head.Children.Add(browse);
            root.Children.Add(head);

            int count = 0;
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i].GameId != gameId) continue;
                count++;
                root.Children.Add(ProfileRow(profiles[i]));
            }
            if (count == 0)
            {
                TextBlock none = new TextBlock();
                none.Text = Repo.T("ui.noProfiles");
                none.Style = (Style)FindResource("Body");
                none.Margin = new Thickness(2, 10, 0, 0);
                root.Children.Add(none);
            }
            Button again = ActionButton(Repo.T("ui.scanAgain"), "GhostBtn");
            again.HorizontalAlignment = HorizontalAlignment.Left;
            again.Margin = new Thickness(0, 2, 0, 10);
            again.Click += delegate { profiles = new List<SaveProfile>(); scanning = true; ShowGameSelect(); RunScan(); };
            root.Children.Add(again);
            if (scanning)
            {
                TextBlock scan = new TextBlock();
                scan.Text = Repo.T("ui.scanning");
                scan.Style = (Style)FindResource("Body");
                scan.Margin = new Thickness(2, 12, 0, 0);
                root.Children.Add(scan);
            }
            SetContent(sv);
            UpdateStatus();
        }

        UIElement ProfileRow(SaveProfile p)
        {
            Border row = new Border();
            row.CornerRadius = new CornerRadius(14);
            row.Background = CPanel;
            row.BorderBrush = CStroke;
            row.BorderThickness = new Thickness(1);
            row.Padding = new Thickness(16, 13, 16, 13);
            row.Margin = new Thickness(0, 0, 0, 10);
            row.Cursor = Cursors.Hand;

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border badge = new Border();
            badge.CornerRadius = new CornerRadius(9);
            badge.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x23, 0x28));
            badge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x35));
            badge.BorderThickness = new Thickness(1);
            badge.Padding = new Thickness(10, 5, 10, 5);
            badge.VerticalAlignment = VerticalAlignment.Center;
            TextBlock bt = new TextBlock();
            bt.Text = p.Platform;
            bt.FontSize = 11;
            bt.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xAC, 0xB2));
            badge.Child = bt;
            grid.Children.Add(badge);

            StackPanel texts = new StackPanel();
            texts.Margin = new Thickness(14, 0, 10, 0);
            texts.VerticalAlignment = VerticalAlignment.Center;
            TextBlock account = new TextBlock();
            account.Text = p.Account;
            account.FontSize = 13.5;
            account.FontWeight = FontWeights.SemiBold;
            account.Foreground = CText;
            TextBlock path = new TextBlock();
            path.Text = p.Path;
            path.FontSize = 10.5;
            path.Foreground = CDim;
            path.TextTrimming = TextTrimming.CharacterEllipsis;
            path.Margin = new Thickness(0, 3, 0, 0);
            path.MaxWidth = 620;
            texts.Children.Add(account);
            texts.Children.Add(path);
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);

            StackPanel right = new StackPanel();
            right.VerticalAlignment = VerticalAlignment.Center;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            TextBlock sum = new TextBlock();
            sum.Text = SaveIO.Summary(p.Path, p.GameId);
            sum.FontSize = 13;
            sum.FontWeight = FontWeights.SemiBold;
            sum.Foreground = CAmber;
            sum.HorizontalAlignment = HorizontalAlignment.Right;
            sum.FlowDirection = FlowDirection.LeftToRight;
            TextBlock date = new TextBlock();
            date.Text = p.Modified.ToString("yyyy-MM-dd HH:mm");
            date.FontSize = 10.5;
            date.Foreground = CDim;
            date.HorizontalAlignment = HorizontalAlignment.Right;
            date.FlowDirection = FlowDirection.LeftToRight;
            right.Children.Add(sum);
            right.Children.Add(date);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            row.Child = grid;
            row.MouseEnter += delegate { row.Background = CPanelHi; row.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x47)); };
            row.MouseLeave += delegate { row.Background = CPanel; row.BorderBrush = CStroke; };
            row.MouseLeftButtonUp += delegate { ChooseProfile(p); };
            return row;
        }

        void ChooseProfile(SaveProfile p)
        {
            SaveIO.BackupOncePerSession(p.Path);
            if (!SaveIO.Load(p.Path))
            {
                EnterManual(p.Path, p.GameId);
                return;
            }
            Repo.ManualMode = false;
            SaveIO.AutoBackup(p.Path);
            Settings.LastGame = Repo.CurrentGame.Id;
            Settings.LastProfile = p.Path;
            Settings.Save();
            view = "main";
            currentCat = "overview";
            SetSidebar(true);
            BuildAll();
        }

        void RunScan()
        {
            scanning = true;
            if (view == "game" || view == "profile") UpdateChooser();
            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                List<SaveProfile> list = null;
                try { list = Scanner.Scan(); }
                catch { list = new List<SaveProfile>(); }
                Dispatcher.Invoke(new Action(delegate()
                {
                    profiles = list;
                    scanning = false;
                    if (view == "game" || view == "profile") UpdateChooser();
                }));
            });
            t.IsBackground = true;
            t.Start();
        }

        UIElement NearestCard(int ep)
        {
            Border panel = new Border();
            panel.CornerRadius = new CornerRadius(16);
            panel.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x23, 0x28));
            panel.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x47));
            panel.BorderThickness = new Thickness(1);
            panel.Padding = new Thickness(18, 13, 18, 12);
            StackPanel sp = new StackPanel();
            panel.Child = sp;

            TextBlock title = new TextBlock();
            title.Text = Repo.T("ui.continueHere") + "  ·  " + Repo.EpName(ep);
            title.FontSize = 13;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = CText;
            sp.Children.Add(title);

            int shown = 0;
            for (int i = 0; i < Repo.Items.Count && shown < 3; i++)
            {
                ItemData it = Repo.Items[i];
                if (it.ep != ep || Repo.IsCollected(it)) continue;
                shown++;
                TextBlock line = new TextBlock();
                line.Text = "•  " + (string.IsNullOrEmpty(it.n) ? Repo.CatName(it.cat) + " #" + it.id : it.n) +
                            "   —   " + Repo.SecName(it) + "  ·  " + it.Loc(Repo.Lang);
                line.FontSize = 11.5;
                line.Foreground = CMuted;
                line.TextWrapping = TextWrapping.Wrap;
                line.Margin = new Thickness(0, 5, 0, 0);
                sp.Children.Add(line);
            }
            if (shown == 0)
            {
                TextBlock done = new TextBlock();
                done.Text = Repo.T("ui.nothingNearby");
                done.FontSize = 12;
                done.Foreground = CGreen;
                done.Margin = new Thickness(0, 6, 0, 0);
                sp.Children.Add(done);
            }
            return panel;
        }

        UIElement ManualBanner()
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(12);
            b.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x21, 0x12));
            b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x47));
            b.BorderThickness = new Thickness(1);
            b.Padding = new Thickness(14, 10, 14, 10);
            b.Margin = new Thickness(0, 12, 0, 0);
            TextBlock t = new TextBlock();
            t.Text = Repo.T("ui.manualBanner");
            t.FontSize = 12;
            t.Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xA4, 0x41));
            t.TextWrapping = TextWrapping.Wrap;
            b.Child = t;
            return b;
        }

        void EnterManual(string path, string gameGuess)
        {
            StopSaveWatcher();
            Repo.ConfigPath = path;
            Repo.ConfigStamp = DateTime.Now;
            string g = gameGuess;
            if (g != "aw" && g != "an")
            {
                string low = path.ToLowerInvariant();
                g = (low.Contains("202750") || low.Contains("americannightmare")) ? "an" : "aw";
            }
            Repo.SetGame(g);
            Repo.Found = new Dictionary<string, List<int>>();
            Repo.Pages = new List<int>();
            Repo.PagesNm = new List<int>();
            Repo.ANPages = new int[0];
            Repo.ManualMode = true;
            Settings.LastGame = g;
            Settings.LastProfile = path;
            Settings.Save();
            view = "main";
            currentCat = "overview";
            SetSidebar(true);
            BuildAll();
            MessageBox.Show(this,
                Repo.T("ui.manualModeMsg"),
                Repo.T("ui.manualModeTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        UIElement EpisodeSummary()
        {
            Border panel = new Border();
            panel.CornerRadius = new CornerRadius(16);
            panel.Background = CPanel;
            panel.BorderBrush = CStroke;
            panel.BorderThickness = new Thickness(1);
            panel.Padding = new Thickness(18, 14, 18, 8);
            StackPanel sp = new StackPanel();
            panel.Child = sp;

            List<int> eps = new List<int>();
            for (int i = 0; i < Repo.Items.Count; i++)
                if (!eps.Contains(Repo.Items[i].ep)) eps.Add(Repo.Items[i].ep);
            eps.Sort();

            foreach (int ep in eps)
            {
                int total = 0, have = 0;
                for (int i = 0; i < Repo.Items.Count; i++)
                {
                    if (Repo.Items[i].ep != ep) continue;
                    total++;
                    if (Repo.IsCollected(Repo.Items[i])) have++;
                }
                Grid row = new Grid();
                row.Margin = new Thickness(0, 0, 0, 9);
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });

                TextBlock name = new TextBlock();
                name.Text = Repo.EpName(ep);
                name.FontSize = 12;
                name.Foreground = CText;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(name);

                Grid bar = ProgressBar(total > 0 ? (double)have / total : 0, 6, CAmber);
                bar.VerticalAlignment = VerticalAlignment.Center;
                bar.Margin = new Thickness(0, 0, 12, 0);
                Grid.SetColumn(bar, 1);
                row.Children.Add(bar);

                TextBlock cnt = new TextBlock();
                cnt.Text = have + " / " + total;
                cnt.FontSize = 11;
                cnt.Foreground = CMuted;
                cnt.FlowDirection = FlowDirection.LeftToRight;
                cnt.HorizontalAlignment = HorizontalAlignment.Right;
                cnt.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(cnt, 2);
                row.Children.Add(cnt);
                sp.Children.Add(row);
            }
            return panel;
        }

        void LogCrash(Exception ex)
        {
            try
            {
                string path = System.IO.Path.Combine(SaveIO.AppDir(), "log.txt");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
                sb.AppendLine(ex == null ? "unknown error" : ex.ToString());
                sb.AppendLine();
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        void OnShortcut(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5) { UpdateFromSave(null, null); e.Handled = true; return; }
            if (e.Key == Key.Escape)
            {
                if (view == "main" && currentCat != "overview")
                {
                    currentCat = "overview";
                    BuildNav();
                    ShowOverview();
                    e.Handled = true;
                }
                else if (view == "main")
                {
                    ShowGameSelect();
                    e.Handled = true;
                }
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F && searchBox != null)
            {
                searchBox.Focus();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.B)
            {
                DoBackup();
                e.Handled = true;
            }
        }

        FileSystemWatcher saveWatcher;
        DispatcherTimer saveDebounce;

        void StartSaveWatcher(string path)
        {
            StopSaveWatcher();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                string dir = System.IO.Path.GetDirectoryName(path);
                saveWatcher = new FileSystemWatcher(dir, System.IO.Path.GetFileName(path));
                saveWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName;
                saveWatcher.EnableRaisingEvents = true;
                FileSystemEventHandler h = delegate { ScheduleReload(); };
                saveWatcher.Changed += h;
                saveWatcher.Created += h;
                saveWatcher.Renamed += delegate { ScheduleReload(); };
            }
            catch { }
        }

        void StopSaveWatcher()
        {
            try
            {
                if (saveWatcher != null)
                {
                    saveWatcher.EnableRaisingEvents = false;
                    saveWatcher.Dispose();
                    saveWatcher = null;
                }
            }
            catch { }
        }

        void ScheduleReload()
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (saveDebounce == null)
                    {
                        saveDebounce = new DispatcherTimer();
                        saveDebounce.Interval = TimeSpan.FromMilliseconds(900);
                        saveDebounce.Tick += delegate { saveDebounce.Stop(); ReloadFromWatcher(); };
                    }
                    saveDebounce.Stop();
                    saveDebounce.Start();
                }));
            }
            catch { }
        }

        void ReloadFromWatcher()
        {
            if (Repo.ConfigPath == null || view != "main") return;
            double off = listHost != null ? listHost.VerticalOffset : 0;
            if (!SaveIO.Load(Repo.ConfigPath)) return;
            Repo.ManualMode = false;
            BuildNav();
            if (currentCat == "overview") ShowOverview(); else ShowCategory(currentCat);
            if (listHost != null) listHost.ScrollToVerticalOffset(off);
            ToastMsg(Repo.T("ui.updatedFromSave"));
        }

        Grid aboutOverlay;

        void ShowAbout()
        {
            HideHoverPreview();
            if (aboutOverlay != null) return;
            int backups = 0;
            try
            {
                string dir = System.IO.Path.Combine(SaveIO.AppDir(), "Backups");
                if (Directory.Exists(dir)) backups = Directory.GetFiles(dir, "*.bak").Length;
            }
            catch { }

            Grid overlay = new Grid();
            overlay.Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x07, 0x09, 0x0B));
            overlay.Focusable = true;
            overlay.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) CloseAbout(); };
            overlay.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (e.OriginalSource == overlay) CloseAbout(); };

            Border card = new Border();
            card.Width = 640;
            card.MaxHeight = 620;
            card.Background = new SolidColorBrush(Color.FromRgb(0x12, 0x15, 0x18));
            card.BorderBrush = CStroke;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(16);
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Center;
            card.Margin = new Thickness(24);

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });

            Grid head = new Grid();
            StackPanel ht = new StackPanel();
            ht.Margin = new Thickness(24, 0, 0, 0);
            ht.VerticalAlignment = VerticalAlignment.Center;
            TextBlock t1 = new TextBlock();
            t1.Text = Repo.T("app.title");
            t1.FontSize = 16;
            t1.FontWeight = FontWeights.SemiBold;
            t1.Foreground = CText;
            TextBlock t2 = new TextBlock();
            t2.Text = "v2.6.6";
            t2.FontSize = 11;
            t2.Foreground = CDim;
            ht.Children.Add(t1);
            ht.Children.Add(t2);
            head.Children.Add(ht);
            Button x = new Button();
            x.Style = (Style)FindResource("WinBtn");
            x.Content = "\uE8BB";
            x.FontFamily = new FontFamily("Segoe MDL2 Assets");
            x.FontSize = 10;
            x.HorizontalAlignment = HorizontalAlignment.Right;
            x.Margin = new Thickness(0, 0, 8, 0);
            x.Click += delegate { CloseAbout(); };
            head.Children.Add(x);
            root.Children.Add(head);

            StackPanel body = new StackPanel();
            body.Margin = new Thickness(24, 0, 24, 24);

            TextBlock created = new TextBlock();
            created.Text = Repo.T("ui.aboutCreated") + " Khalid Alsahari";
            created.FontSize = 13;
            created.Foreground = CText;
            created.Margin = new Thickness(0, 0, 0, 6);
            body.Children.Add(created);

            body.Children.Add(AboutRow(Repo.T("ui.aboutWebsite"), "ippyz.com", "https://ippyz.com"));
            body.Children.Add(AboutRow(Repo.T("ui.aboutInstagram"), "@ippyz", "https://www.instagram.com/ippyz"));

            body.Children.Add(AboutDivider());
            body.Children.Add(AboutSection(Repo.T("ui.aboutGame")));
            body.Children.Add(AboutText(Repo.CurrentGame != null ? Repo.T(Repo.CurrentGame.TitleKey) : "—"));
            body.Children.Add(AboutText(Repo.ConfigPath == null ? "—" : Repo.ConfigPath));
            body.Children.Add(AboutText(Repo.T("ui.aboutBackups") + ": " + backups));

            body.Children.Add(AboutDivider());
            body.Children.Add(AboutSection(Repo.T("ui.aboutSources")));
            body.Children.Add(AboutRow("Pandallax", "pandallax.com", "https://www.pandallax.com/"));
            body.Children.Add(AboutRow(Repo.T("ui.videoGuide"), "youtube.com/watch?v=3thI0eUZKtM", "https://www.youtube.com/watch?v=3thI0eUZKtM&t=754s"));
            body.Children.Add(AboutRow("PCGamingWiki", "Alan Wake", "https://www.pcgamingwiki.com/wiki/Alan_Wake"));
            body.Children.Add(AboutRow("PCGamingWiki", "American Nightmare", "https://www.pcgamingwiki.com/wiki/Alan_Wake%27s_American_Nightmare"));
            body.Children.Add(AboutRow("Fandom", "alanwake.fandom.com", "https://alanwake.fandom.com/"));

            body.Children.Add(AboutDivider());
            body.Children.Add(AboutSection(Repo.T("ui.aboutLocations")));
            body.Children.Add(AboutText("Steam: userdata\\<id>\\108710 (AW) / 202750 (AN)"));
            body.Children.Add(AboutText("Epic: Documents\\Remedy\\..._EGS_Version"));
            body.Children.Add(AboutText("GOG: Documents\\Remedy\\..._GOG_Version"));
            body.Children.Add(AboutText("Store: Documents\\Remedy\\..._MS_Version"));
            body.Children.Add(AboutText("Documents\\Remedy\\AlanWake | AmericanNightmare"));

            TextBlock ro = new TextBlock();
            ro.Text = Repo.T("ui.statusReadOnly");
            ro.FontSize = 11;
            ro.Foreground = CDim;
            ro.Margin = new Thickness(0, 16, 0, 0);
            body.Children.Add(ro);

            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.Background = Brushes.Transparent;
            sv.Content = body;
            Grid.SetRow(sv, 1);
            root.Children.Add(sv);

            Grid foot = new Grid();
            Button close = new Button();
            close.Style = (Style)FindResource("GhostBtn");
            close.Content = Repo.T("ui.aboutClose");
            close.Padding = new Thickness(18, 8, 18, 8);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 0, 24, 0);
            close.VerticalAlignment = VerticalAlignment.Center;
            close.Click += delegate { CloseAbout(); };
            foot.Children.Add(close);
            Grid.SetRow(foot, 2);
            root.Children.Add(foot);

            card.Child = root;
            overlay.Children.Add(card);
            Grid.SetRowSpan(overlay, 3);
            Panel.SetZIndex(overlay, 200);
            RootGrid.Children.Add(overlay);
            aboutOverlay = overlay;
            overlay.Focus();
        }

        void CloseAbout()
        {
            if (aboutOverlay != null)
            {
                RootGrid.Children.Remove(aboutOverlay);
                aboutOverlay = null;
            }
        }

        UIElement AboutRow(string label, string value, string url)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock l = new TextBlock();
            l.Text = label;
            l.FontSize = 12;
            l.Foreground = CDim;
            l.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(l);
            TextBlock v = new TextBlock();
            v.Text = value;
            v.FontSize = 12.5;
            v.Foreground = CAmber;
            v.Cursor = Cursors.Hand;
            v.TextDecorations = TextDecorations.Underline;
            v.VerticalAlignment = VerticalAlignment.Center;
            v.MouseLeftButtonUp += delegate { OpenUrl(url); };
            v.ToolTip = url;
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            g.Margin = new Thickness(0, 3, 0, 3);
            return g;
        }

        UIElement AboutSection(string text)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 11;
            t.Foreground = CDim;
            t.Margin = new Thickness(0, 16, 0, 6);
            return t;
        }

        UIElement AboutText(string text)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 11.5;
            t.Foreground = CMuted;
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(150, 2, 0, 2);
            t.FontFamily = new FontFamily("Consolas");
            return t;
        }

        UIElement AboutDivider()
        {
            Border b = new Border();
            b.Height = 1;
            b.Background = CStroke;
            b.Margin = new Thickness(0, 16, 0, 0);
            return b;
        }

        void ExportReport()
        {
            try
            {
                using (System.Windows.Forms.SaveFileDialog dlg = new System.Windows.Forms.SaveFileDialog())
                {
                    dlg.Filter = "Text file|*.txt";
                    dlg.FileName = "AlanWake-report-" + DateTime.Now.ToString("yyyyMMdd") + ".txt";
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("Alan Wake Collectible Tracker — report");
                    sb.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                    sb.AppendLine("Game: " + Repo.T(Repo.CurrentGame.TitleKey));
                    sb.AppendLine("Save: " + (Repo.ConfigPath == null ? "—" : Repo.ConfigPath));
                    sb.AppendLine();
                    foreach (CategoryInfo c in Repo.Categories)
                    {
                        int have = Repo.Have(c.Key), total = Repo.Total(c.Key);
                        sb.AppendLine("== " + Repo.CatName(c.Key) + "  (" + have + " / " + total + ") ==");
                        foreach (ItemData it in Repo.ByCat(c.Key))
                        {
                            bool got = Repo.IsCollected(it);
                            sb.AppendLine((got ? "[x] " : "[ ] ") + Repo.EpName(it.ep) + " · " + Repo.SecName(it) + " · " +
                                          (string.IsNullOrEmpty(it.n) ? Repo.CatName(it.cat) + " #" + it.id : it.n));
                            sb.AppendLine("      " + it.Loc(Repo.Lang));
                        }
                        sb.AppendLine();
                    }
                    File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                    ToastMsg(Repo.T("ui.exported"));
                }
            }
            catch (Exception ex) { ToastMsg(ex.Message); }
        }

        void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(url); }
            catch (Exception ex) { ToastMsg(ex.Message); }
        }

        void OpenGuide(string cat)
        {
            string url = "https://www.pandallax.com/";
            if (cat == "page" || cat == "nm") url = "https://www.pandallax.com/guides/alan-wake---all-manuscript-locations-guide";
            else if (cat == "thermos") url = "https://www.pandallax.com/guides/alan-wake---all-coffee-thermos-locations";
            else if (cat == "chest") url = "https://www.pandallax.com/guides/alan-wake---all-supply-chest-locations-guide";
            else if (cat == "cans") url = "https://www.pandallax.com/guides/alan-wake-all-can-pyramid-collectible-locations";
            else if (cat == "radio") url = "https://www.pandallax.com/guides/alan-wake-all-radio-collectible-locations";
            else if (cat == "tv") url = "https://www.pandallax.com/guides/alan-wake-all-tv-show-locations-guide";
            else if (cat == "sign") url = "https://www.pandallax.com/guides/alan-wake---all-sign-locations-guide";
            else if (cat == "alarm" || cat == "cutout") url = "https://www.pandallax.com/guides/alan-wake-the-signal-dlc-100-achievement-trophy-guide";
            else if (cat == "game") url = "https://www.pandallax.com/guides/alan-wake-the-writer-dlc-100-achievement-trophy-guide";
            else if (cat == "an-page") url = "https://www.pandallax.com/guides";
            OpenUrl(url);
        }

        void OpenBackups()
        {
            try
            {
                string dir = System.IO.Path.Combine(SaveIO.AppDir(), "Backups");
                System.IO.Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", dir);
            }
            catch (Exception ex) { ToastMsg(ex.Message); }
        }

        void SwitchBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowGameSelect();
        }

        void UpdateStatus()
        {
            if (Repo.ConfigPath == null || view != "main")
            {
                StatusText.Text = Repo.T("ui.chooseHint");
                StatusDot.Fill = CAmber;
            }
            else
            {
                StatusText.FlowDirection = FlowDirection.LeftToRight;
                StatusText.Text = Repo.ConfigPath + "   ·   " + Repo.ConfigStamp.ToString("yyyy-MM-dd HH:mm");
                StatusDot.Fill = CGreen;
            }
        }

        /* ---------------- sidebar ---------------- */

        void BuildNav()
        {
            NavPanel.Children.Clear();
            NavPanel.Children.Add(NavItem("overview", "", Repo.T("ui.overview"), Repo.HaveAll(), Repo.TotalAll()));

            Border sep = new Border();
            sep.Height = 1;
            sep.Background = CStroke;
            sep.Margin = new Thickness(6, 10, 6, 10);
            NavPanel.Children.Add(sep);

            for (int i = 0; i < Repo.Categories.Count; i++)
            {
                CategoryInfo c = Repo.Categories[i];
                NavPanel.Children.Add(NavItem(c.Key, c.Icon, Repo.CatName(c.Key), Repo.Have(c.Key), Repo.Total(c.Key)));
            }

            SideBottom.Children.Clear();
            SideBottom.Children.Add(SideAction("", Repo.T("ui.updateSave"), delegate { UpdateFromSave(null, null); }));
            SideBottom.Children.Add(SideAction("", Repo.T("ui.backup"), delegate { DoBackup(); }));
            SideBottom.Children.Add(SideAction("", Repo.T("ui.chooseFile"), delegate { PromptForSave(); }));
            SideBottom.Children.Add(SideAction("", Repo.T("ui.videoGuide"), delegate { OpenUrl("https://www.youtube.com/watch?v=3thI0eUZKtM&t=754s"); }));
            SideBottom.Children.Add(SideAction("", Repo.T("ui.siteGuide"), delegate { OpenUrl("https://www.pandallax.com/"); }));
            SideBottom.Children.Add(SideAction("", Repo.T("ui.openBackups"), delegate { OpenBackups(); }));
            SideBottom.Children.Add(SideAction("", Repo.T("ui.about"), delegate { ShowAbout(); }));

            TextBlock ro = new TextBlock();
            ro.Text = Repo.T("ui.statusReadOnly");
            ro.FontSize = 10.5;
            ro.Foreground = CDim;
            ro.TextWrapping = TextWrapping.Wrap;
            ro.Margin = new Thickness(8, 10, 8, 0);
            SideBottom.Children.Add(ro);
        }

        UIElement NavItem(string cat, string icon, string label, int have, int total)
        {
            Button b = new Button();
            b.Style = (Style)FindResource("NavBtn");
            b.Tag = cat == currentCat ? "on" : "";
            b.MinHeight = 36;
            b.Margin = new Thickness(0);
            b.Padding = new Thickness(0);
            b.Click += delegate { currentCat = cat; epFilter = -1; search = ""; BuildNav(); if (cat == "overview") ShowOverview(); else ShowCategory(cat); };

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid ic = CatIcon(cat, 16, CDim);
            ic.Margin = new Thickness(0, 0, 10, 0);
            g.Children.Add(ic);

            TextBlock tx = new TextBlock();
            tx.Text = label;
            tx.FontSize = 12;
            tx.TextTrimming = TextTrimming.CharacterEllipsis;
            tx.MaxWidth = 160;
            tx.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(tx, 1);
            g.Children.Add(tx);

            TextBlock cnt = new TextBlock();
            cnt.Text = have + "/" + total;
            cnt.FlowDirection = FlowDirection.LeftToRight;
            cnt.FontSize = 10.5;
            cnt.Foreground = cat == currentCat ? CAmber : CDim;
            cnt.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(cnt, 2);
            g.Children.Add(cnt);

            b.Content = g;
            b.ToolTip = label;
            return b;
        }

        Button SideAction(string icon, string text, RoutedEventHandler onClick)
        {
            Button b = new Button();
            b.Style = (Style)FindResource("NavBtn");
            b.Height = 34;
            b.Margin = new Thickness(0, 2, 0, 2);
            b.Click += onClick;
            TextBlock tx = new TextBlock();
            tx.Text = text;
            tx.FontSize = 12;
            tx.VerticalAlignment = VerticalAlignment.Center;
            b.Content = tx;
            return b;
        }

        /* ---------------- overview ---------------- */

        void ShowOverview()
        {
            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            StackPanel root = new StackPanel();
            sv.Content = root;
            sv.Margin = new Thickness(0, 0, -20, 0);
            root.Margin = new Thickness(0, 0, 20, 0);

            root.Children.Add(BuildHero());
            int nearEp = Repo.CurrentEpisode();
            if (nearEp > 0) { root.Children.Add(Spacer(12)); root.Children.Add(NearestCard(nearEp)); }
            root.Children.Add(Spacer(20));

            TextBlock head = new TextBlock();
            head.Text = Repo.T("ui.categories");
            head.Style = (Style)FindResource("H2");
            head.Margin = new Thickness(2, 0, 0, 12);
            root.Children.Add(head);

            UniformGrid grid = new UniformGrid();
            grid.Columns = 3;
            for (int i = 0; i < Repo.Categories.Count; i++)
            {
                CategoryInfo c = Repo.Categories[i];
                Border holder = new Border();
                holder.Margin = new Thickness(0, 0, 12, 12);
                holder.Child = CategoryCard(c);
                grid.Children.Add(holder);
            }
            root.Children.Add(grid);
            root.Children.Add(Spacer(10));

            TextBlock epHead = new TextBlock();
            epHead.Text = Repo.T("ui.byEpisode");
            epHead.Style = (Style)FindResource("H2");
            epHead.Margin = new Thickness(2, 0, 0, 10);
            root.Children.Add(epHead);
            root.Children.Add(EpisodeSummary());
            root.Children.Add(Spacer(14));

            SetContent(sv);
        }

        UIElement BuildHero()
        {
            Border hero = new Border();
            hero.CornerRadius = new CornerRadius(18);
            hero.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x35));
            hero.BorderThickness = new Thickness(1);
            hero.Padding = new Thickness(26, 22, 26, 22);

            LinearGradientBrush bg = new LinearGradientBrush();
            bg.StartPoint = new Point(0, 0);
            bg.EndPoint = new Point(1, 1);
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x17, 0x1B, 0x1F), 0));
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x15, 0x18, 0x1B), 1));
            hero.Background = bg;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            int have = Repo.HaveAll();
            int total = Repo.TotalAll();
            double pct = total > 0 ? (double)have / total : 0;

            g.Children.Add(Donut(pct, have, total));

            StackPanel mid = new StackPanel();
            mid.Margin = new Thickness(26, 0, 26, 0);
            mid.VerticalAlignment = VerticalAlignment.Center;

            TextBlock t1 = new TextBlock();
            t1.Text = Repo.T("ui.overview");
            t1.Style = (Style)FindResource("H1");
            mid.Children.Add(t1);

            TextBlock t2 = new TextBlock();
            t2.Text = Repo.T("ui.sub");
            t2.Style = (Style)FindResource("Body");
            t2.Margin = new Thickness(0, 4, 0, 14);
            mid.Children.Add(t2);

            StackPanel chips = new StackPanel();
            chips.Orientation = Orientation.Horizontal;
            chips.Children.Add(StatChip("", have.ToString(), Repo.T("ui.collected"), CGreen));
            chips.Children.Add(StatChip("", (total - have).ToString(), Repo.T("ui.missingK"), CRed));
            chips.Children.Add(StatChip("", Repo.Categories.Count.ToString(), Repo.T("ui.categories"), CAmber));
            mid.Children.Add(chips);

            Grid.SetColumn(mid, 1);
            g.Children.Add(mid);

            StackPanel actions = new StackPanel();
            actions.VerticalAlignment = VerticalAlignment.Center;
            actions.HorizontalAlignment = HorizontalAlignment.Right;

            Button upd = ActionButton(Repo.T("ui.updateSave"), "AccentBtn");
            upd.HorizontalAlignment = HorizontalAlignment.Stretch;
            upd.Margin = new Thickness(0, 0, 0, 8);
            upd.Click += UpdateFromSave;
            actions.Children.Add(upd);

            Button bak = ActionButton(Repo.T("ui.backup"), "GhostBtn");
            bak.HorizontalAlignment = HorizontalAlignment.Stretch;
            bak.Margin = new Thickness(0, 0, 0, 8);
            bak.Click += delegate { DoBackup(); };
            actions.Children.Add(bak);

            Button pick = ActionButton(Repo.T("ui.chooseFile"), "GhostBtn");
            pick.HorizontalAlignment = HorizontalAlignment.Stretch;
            pick.Click += delegate { PromptForSave(); };
            actions.Children.Add(pick);

            TextBlock safe = new TextBlock();
            safe.Text = Repo.T("ui.safeLine");
            safe.FontSize = 10;
            safe.Foreground = CDim;
            safe.TextWrapping = TextWrapping.Wrap;
            safe.Margin = new Thickness(0, 10, 0, 0);
            safe.MaxWidth = 220;
            actions.Children.Add(safe);

            Grid.SetColumn(actions, 2);
            g.Children.Add(actions);

            hero.Child = g;
            return hero;
        }

        UIElement Donut(double pct, int have, int total)
        {
            Grid wrap = new Grid();
            wrap.Width = 158;
            wrap.Height = 158;
            wrap.VerticalAlignment = VerticalAlignment.Center;

            Canvas cv = new Canvas();
            cv.Width = 158;
            cv.Height = 158;

            Ellipse ring = new Ellipse();
            ring.Width = 138;
            ring.Height = 138;
            ring.Stroke = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x35));
            ring.StrokeThickness = 9;
            Canvas.SetLeft(ring, 10);
            Canvas.SetTop(ring, 10);
            cv.Children.Add(ring);

            if (pct > 0.001)
            {
                double r = 69;
                Point center = new Point(79, 79);
                double start = -90;
                double sweep = 359.99 * Math.Min(1, pct);
                PathFigure fig = new PathFigure();
                fig.StartPoint = OnCircle(center, r, start);
                ArcSegment arc = new ArcSegment();
                arc.Point = OnCircle(center, r, start + sweep);
                arc.Size = new Size(r, r);
                arc.IsLargeArc = pct > 0.5;
                arc.SweepDirection = SweepDirection.Clockwise;
                arc.IsStroked = true;
                fig.Segments.Add(arc);
                PathGeometry geo = new PathGeometry();
                geo.Figures.Add(fig);
                Path p = new Path();
                p.Data = geo;
                p.Stroke = CAmber;
                p.StrokeThickness = 9;
                p.StrokeStartLineCap = PenLineCap.Round;
                p.StrokeEndLineCap = PenLineCap.Round;
                cv.Children.Add(p);
            }

            wrap.Children.Add(cv);

            StackPanel centerBox = new StackPanel();
            centerBox.VerticalAlignment = VerticalAlignment.Center;
            centerBox.HorizontalAlignment = HorizontalAlignment.Center;
            TextBlock pc = new TextBlock();
            pc.Text = Math.Round(pct * 100) + "%";
            pc.FontSize = 30;
            pc.FontWeight = FontWeights.SemiBold;
            pc.Foreground = CText;
            pc.HorizontalAlignment = HorizontalAlignment.Center;
            TextBlock cnt = new TextBlock();
            cnt.Text = have + " / " + total;
            cnt.FlowDirection = FlowDirection.LeftToRight;
            cnt.FontSize = 12;
            cnt.Foreground = CMuted;
            cnt.HorizontalAlignment = HorizontalAlignment.Center;
            cnt.Margin = new Thickness(0, 2, 0, 0);
            centerBox.Children.Add(pc);
            centerBox.Children.Add(cnt);
            wrap.Children.Add(centerBox);
            return wrap;
        }

        static Point OnCircle(Point c, double r, double deg)
        {
            double rad = deg * Math.PI / 180.0;
            return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
        }

        UIElement StatChip(string icon, string value, string label, SolidColorBrush color)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(11);
            b.Background = new SolidColorBrush(Color.FromRgb(0x17, 0x1B, 0x1F));
            b.BorderBrush = CStroke;
            b.BorderThickness = new Thickness(1);
            b.Padding = new Thickness(12, 8, 14, 8);
            b.Margin = new Thickness(0, 0, 8, 0);

            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            TextBlock ic = new TextBlock();
            ic.Text = icon;
            ic.FontSize = 13;
            ic.Foreground = color;
            ic.VerticalAlignment = VerticalAlignment.Center;
            ic.Margin = new Thickness(0, 0, 8, 0);
            TextBlock v = new TextBlock();
            v.Text = value;
            v.FlowDirection = FlowDirection.LeftToRight;
            v.FontSize = 15;
            v.FontWeight = FontWeights.SemiBold;
            v.Foreground = CText;
            v.VerticalAlignment = VerticalAlignment.Center;
            TextBlock l = new TextBlock();
            l.Text = "  " + label;
            l.FontSize = 11.5;
            l.Foreground = CMuted;
            l.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(ic);
            sp.Children.Add(v);
            sp.Children.Add(l);
            b.Child = sp;
            return b;
        }

        UIElement CategoryCard(CategoryInfo c)
        {
            int have = Repo.Have(c.Key);
            int total = Repo.Total(c.Key);
            int missing = total - have;
            double pct = total > 0 ? (double)have / total : 0;

            Button b = new Button();
            b.Style = (Style)FindResource("CardBtn");
            b.MinHeight = 102;
            b.Click += delegate { currentCat = c.Key; epFilter = -1; search = ""; BuildNav(); ShowCategory(c.Key); };

            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());

            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition());

            Grid cardIcon = CatIcon(c.Key, 18, CDim);
            cardIcon.Margin = new Thickness(0, 0, 10, 0);
            cardIcon.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(cardIcon);

            StackPanel texts = new StackPanel();
            texts.VerticalAlignment = VerticalAlignment.Center;
            texts.HorizontalAlignment = HorizontalAlignment.Left;
            TextBlock nm = new TextBlock();
            nm.Text = Repo.CatName(c.Key);
            nm.TextWrapping = TextWrapping.Wrap;
            nm.MaxWidth = 210;
            nm.FontSize = 12.5;
            nm.FontWeight = FontWeights.SemiBold;
            nm.Foreground = CText;
            nm.HorizontalAlignment = HorizontalAlignment.Left;
            TextBlock cnt = new TextBlock();
            cnt.Text = have + " / " + total;
            cnt.FlowDirection = FlowDirection.LeftToRight;
            cnt.FontSize = 11.5;
            cnt.Foreground = CMuted;
            cnt.Margin = new Thickness(0, 2, 0, 0);
            cnt.HorizontalAlignment = HorizontalAlignment.Left;
            texts.Children.Add(nm);
            texts.Children.Add(cnt);
            Grid.SetColumn(texts, 1);
            top.Children.Add(texts);

            if (missing > 0)
            {
                TextBlock bt = new TextBlock();
                bt.Text = "-" + missing;
                bt.FlowDirection = FlowDirection.LeftToRight;
                bt.FontSize = 10.5;
                bt.Foreground = CDim;
                bt.HorizontalAlignment = HorizontalAlignment.Right;
                bt.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumnSpan(bt, 2);
                top.Children.Add(bt);
            }

            g.Children.Add(top);

            Grid pb = ProgressBar(pct, 5, CatColor(c.Key));
            pb.VerticalAlignment = VerticalAlignment.Bottom;
            pb.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(pb, 1);
            g.Children.Add(pb);

            b.Content = g;
            return b;
        }

        static SolidColorBrush CatColor(string cat)
        {
            return CAmber;
        }

        /* ---------------- category view ---------------- */

        void ShowCategory(string cat)
        {
            CategoryInfo info = null;
            for (int i = 0; i < Repo.Categories.Count; i++) if (Repo.Categories[i].Key == cat) info = Repo.Categories[i];
            if (info == null) { ShowOverview(); return; }

            int have = Repo.Have(cat);
            int total = Repo.Total(cat);

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());

            /* header */
            Grid header = new Grid();
            header.Margin = new Thickness(0, 0, 0, 14);
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Button back = new Button();
            back.Style = (Style)FindResource("GhostBtn");
            back.Content = "‹";
            back.FontSize = 18;
            back.Padding = new Thickness(12, 0, 12, 4);
            back.Height = 40;
            back.Margin = new Thickness(0, 0, 10, 0);
            back.Click += delegate { currentCat = "overview"; BuildNav(); ShowOverview(); };
            header.Children.Add(back);

            Grid headIcon = CatIcon(cat, 22, CDim);
            headIcon.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(headIcon, 1);
            header.Children.Add(headIcon);

            StackPanel titleBox = new StackPanel();
            titleBox.Margin = new Thickness(10, 0, 0, 0);
            titleBox.VerticalAlignment = VerticalAlignment.Center;
            TextBlock title = new TextBlock();
            title.Text = Repo.CatName(cat);
            title.Style = (Style)FindResource("H1");
            title.FontSize = 21;
            TextBlock sub = new TextBlock();
            sub.Text = Ltr(have + " / " + total) + "   ·   " + Repo.T("ui.missingK") + " " + Ltr((total - have).ToString());
            sub.Style = (Style)FindResource("Body");
            sub.Margin = new Thickness(0, 2, 0, 0);
            titleBox.Children.Add(title);
            titleBox.Children.Add(sub);
            Grid.SetColumn(titleBox, 2);
            header.Children.Add(titleBox);

            StackPanel headActions = new StackPanel();
            headActions.Orientation = Orientation.Horizontal;
            headActions.VerticalAlignment = VerticalAlignment.Center;
            Button guideBtn = ActionButton(Repo.T("ui.guideBtn"), "GhostBtn");
            guideBtn.Margin = new Thickness(0, 0, 6, 0);
            guideBtn.Click += delegate { OpenGuide(cat); };
            headActions.Children.Add(guideBtn);
            Button copy = ActionButton(Repo.T("ui.copyBtn"), "GhostBtn");
            copy.Margin = new Thickness(0, 0, 6, 0);
            copy.Click += delegate { CopyMissing(cat); };
            headActions.Children.Add(copy);
            Button export = ActionButton(Repo.T("ui.export"), "GhostBtn");
            export.Click += delegate { ExportReport(); };
            headActions.Children.Add(export);
            Grid.SetColumn(headActions, 3);
            header.Children.Add(headActions);

            root.Children.Add(header);

            /* toolbar */
            Grid bar = new Grid();
            bar.Margin = new Thickness(0, 0, 0, 10);
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid searchWrap = new Grid();
            searchBox = new TextBox();
            searchBox.Style = (Style)FindResource("SearchBox");
            searchBox.Text = search;
            searchBox.TextChanged += delegate
            {
                search = searchBox.Text;
                if (placeholder != null) placeholder.Visibility = search.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                RenderItems();
            };
            searchWrap.Children.Add(searchBox);

            placeholder = new TextBlock();
            placeholder.Text = Repo.T("ui.search");
            placeholder.FontSize = 12.5;
            placeholder.Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x76, 0x7C));
            placeholder.Margin = new Thickness(11, 0, 0, 0);
            placeholder.VerticalAlignment = VerticalAlignment.Center;
            placeholder.IsHitTestVisible = false;
            if (search.Length > 0) placeholder.Visibility = Visibility.Collapsed;
            searchWrap.Children.Add(placeholder);
            bar.Children.Add(searchWrap);

            StackPanel modes = new StackPanel();
            modes.Orientation = Orientation.Horizontal;
            modes.HorizontalAlignment = HorizontalAlignment.Right;
            modes.VerticalAlignment = VerticalAlignment.Center;
            modes.Children.Add(ModeChip("missing", Repo.T("ui.modeMissing")));
            modes.Children.Add(ModeChip("all", Repo.T("ui.modeAll")));
            modes.Children.Add(ModeChip("have", Repo.T("ui.modeHave")));
            Grid.SetColumn(modes, 1);
            bar.Children.Add(modes);

            StackPanel sortWrap = new StackPanel();
            sortWrap.Orientation = Orientation.Horizontal;
            sortWrap.VerticalAlignment = VerticalAlignment.Center;
            sortWrap.Margin = new Thickness(8, 0, 0, 0);
            ToggleButton sortEp = new ToggleButton();
            sortEp.Style = (Style)FindResource("ChipToggle");
            sortEp.Content = Repo.T("ui.sortEp");
            sortEp.IsChecked = sortBy != "num";
            sortEp.Margin = new Thickness(0, 0, 4, 0);
            ToggleButton sortNum = new ToggleButton();
            sortNum.Style = (Style)FindResource("ChipToggle");
            sortNum.Content = Repo.T("ui.sortNum");
            sortNum.IsChecked = sortBy == "num";
            sortEp.Click += delegate { sortBy = "ep"; Settings.SortBy = sortBy; Settings.Save(); sortEp.IsChecked = true; sortNum.IsChecked = false; RenderItems(); };
            sortNum.Click += delegate { sortBy = "num"; Settings.SortBy = sortBy; Settings.Save(); sortNum.IsChecked = true; sortEp.IsChecked = false; RenderItems(); };
            sortWrap.Children.Add(sortEp);
            sortWrap.Children.Add(sortNum);
            Grid.SetColumn(sortWrap, 2);
            bar.Children.Add(sortWrap);

            Grid.SetColumn(bar, 0);
            Grid.SetColumnSpan(bar, 4);
            Grid.SetRow(bar, 1);
            root.Children.Add(bar);

            /* list */
            StackPanel content = new StackPanel();
            content.Margin = new Thickness(0, 0, 0, 0);

            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.Content = content;
            sv.Padding = new Thickness(0);
            sv.Margin = new Thickness(0, 0, -20, 0);
            content.Margin = new Thickness(0, 0, 20, 0);
            Grid.SetRow(sv, 2);
            root.Children.Add(sv);

            listPanel = content;
            listHost = sv;
            RenderItems();
            SetContent(root);
        }

        StackPanel listPanel;
        ScrollViewer listHost;

        ToggleButton ModeChip(string key, string label)
        {
            ToggleButton tb = new ToggleButton();
            tb.Style = (Style)FindResource("ChipToggle");
            tb.Content = label;
            tb.Tag = key;
            tb.IsChecked = mode == key;
            tb.Margin = new Thickness(0, 0, 6, 0);
            tb.Click += delegate
            {
                mode = key;
                Settings.Mode = key;
                Settings.Save();
                RenderItems();
                if (tb.Parent is StackPanel)
                {
                    foreach (object o in ((StackPanel)tb.Parent).Children)
                    {
                        ToggleButton t = o as ToggleButton;
                        if (t != null) t.IsChecked = (string)t.Tag == mode;
                    }
                }
            };
            return tb;
        }

        void RenderItems()
        {
            if (listPanel == null) return;
            listPanel.Children.Clear();
            List<ItemData> items = Filtered();
            if (items.Count == 0)
            {
                Border empty = new Border();
                empty.Margin = new Thickness(0, 40, 0, 0);
                StackPanel sp = new StackPanel();
                sp.HorizontalAlignment = HorizontalAlignment.Center;
                TextBlock big = new TextBlock();
                big.Text = "";
                big.FontSize = 34;
                big.HorizontalAlignment = HorizontalAlignment.Center;
                big.Opacity = 0.85;
                TextBlock msg = new TextBlock();
                msg.Text = mode == "missing" ? Repo.T("ui.nothingLeft") : Repo.T("ui.empty");
                msg.FontSize = 13;
                msg.Foreground = CMuted;
                msg.Margin = new Thickness(0, 10, 0, 0);
                msg.TextAlignment = TextAlignment.Center;
                sp.Children.Add(big);
                sp.Children.Add(msg);
                empty.Child = sp;
                listPanel.Children.Add(empty);
                return;
            }

            int lastEp = -1;
            string lastSk = null;
            for (int i = 0; i < items.Count; i++)
            {
                ItemData it = items[i];
                if (it.ep != lastEp)
                {
                    lastEp = it.ep;
                    lastSk = null;
                    listPanel.Children.Add(EpHeader(it.ep, items));
                }
                if (it.sk != lastSk)
                {
                    lastSk = it.sk;
                    listPanel.Children.Add(PartHeader(it));
                }
                listPanel.Children.Add(ItemRow(it));
            }
        }

        List<ItemData> Filtered()
        {
            List<ItemData> all = Repo.ByCat(currentCat);
            List<ItemData> outList = new List<ItemData>();
            string q = search == null ? "" : search.Trim().ToLowerInvariant();
            for (int i = 0; i < all.Count; i++)
            {
                ItemData it = all[i];
                bool have = Repo.IsCollected(it);
                if (mode == "missing" && have) continue;
                if (mode == "have" && !have) continue;
                if (epFilter >= 0 && it.ep != epFilter) continue;
                if (q.Length > 0)
                {
                    string hay = (Repo.CatName(it.cat) + " " + (it.n == null ? "" : it.n) + " " + it.Loc(Repo.Lang) + " " + Repo.EpName(it.ep) + " " + Repo.SecName(it) + " #" + it.id + " " + Repo.DisplayNumber(it)).ToLowerInvariant();
                    if (hay.IndexOf(q, StringComparison.Ordinal) < 0) continue;
                }
                outList.Add(it);
            }
            if (sortBy == "num")
                outList.Sort(delegate(ItemData a, ItemData b) { return a.id.CompareTo(b.id); });
            return outList;
        }

        UIElement EpHeader(int ep, List<ItemData> items)
        {
            int cnt = 0;
            for (int i = 0; i < items.Count; i++) if (items[i].ep == ep) cnt++;

            Grid g = new Grid();
            g.Margin = new Thickness(0, 18, 0, 8);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock t = new TextBlock();
            t.Text = Repo.EpName(ep);
            t.FontSize = 15.5;
            t.FontWeight = FontWeights.SemiBold;
            t.Foreground = CText;
            g.Children.Add(t);
            TextBlock c = new TextBlock();
            c.Text = cnt.ToString();
            c.FlowDirection = FlowDirection.LeftToRight;
            c.FontSize = 11;
            c.Foreground = CDim;
            c.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(c, 1);
            g.Children.Add(c);
            return g;
        }

        UIElement PartHeader(ItemData it)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 8, 0, 6);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            Border bar = new Border();
            bar.Width = 3;
            bar.Height = 12;
            bar.CornerRadius = new CornerRadius(2);
            bar.Background = CAmber;
            bar.Margin = new Thickness(0, 0, 8, 0);
            bar.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(bar);
            TextBlock t = new TextBlock();
            t.Text = Repo.SecName(it);
            t.FontSize = 11;
            t.Foreground = CDim;
            t.FontWeight = FontWeights.SemiBold;
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            return g;
        }

        UIElement ItemRow(ItemData it)
        {
            bool have = Repo.IsCollected(it);

            Border row = new Border();
            row.CornerRadius = new CornerRadius(13);
            row.Background = CPanel;
            row.BorderBrush = CStroke;
            row.BorderThickness = new Thickness(1);
            row.Padding = new Thickness(12, 11, 14, 11);
            row.Margin = new Thickness(0, 0, 0, 8);
            row.Cursor = Cursors.Hand;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel txt = new StackPanel();
            txt.Margin = new Thickness(2, 0, 10, 0);
            txt.VerticalAlignment = VerticalAlignment.Center;

            StackPanel titleLine = new StackPanel();
            titleLine.Orientation = Orientation.Horizontal;
            TextBlock name = new TextBlock();
            string nm = it.n;
            if (string.IsNullOrEmpty(nm)) nm = Repo.CatName(it.cat) + " #" + it.id;
            name.Text = nm;
            name.FontSize = 13;
            name.FontWeight = FontWeights.SemiBold;
            name.Foreground = CText;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            titleLine.Children.Add(name);

            if (it.cat == "nm")
            {
                Border tag = new Border();
                tag.CornerRadius = new CornerRadius(6);
                tag.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x21, 0x12));
                tag.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x47));
                tag.BorderThickness = new Thickness(1);
                tag.Padding = new Thickness(6, 1, 6, 1);
                tag.Margin = new Thickness(8, 0, 0, 0);
                TextBlock tt = new TextBlock();
                tt.Text = Repo.T("ui.nmOnly");
                tt.FontSize = 9.5;
                tt.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xAC, 0xB2));
                tag.Child = tt;
                titleLine.Children.Add(tag);
            }
            txt.Children.Add(titleLine);

            TextBlock meta = new TextBlock();
            meta.Text = Repo.EpName(it.ep) + "   ·   " + Repo.SecName(it) + (it.cat == "thermos" ? "" : "   ·   " + Ltr("#" + Repo.DisplayNumber(it)));
            meta.FontSize = 10.5;
            meta.Foreground = CDim;
            meta.Margin = new Thickness(0, 2, 0, 0);
            txt.Children.Add(meta);

            TextBlock desc = new TextBlock();
            desc.Text = it.Loc(Repo.Lang);
            desc.FontSize = 11.5;
            desc.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xAC, 0xB2));
            desc.TextWrapping = TextWrapping.Wrap;
            desc.Margin = new Thickness(0, 4, 0, 0);
            desc.LineHeight = 17;
            txt.Children.Add(desc);

            Grid.SetColumn(txt, 1);
            g.Children.Add(txt);

            string imgPath = ItemImagePath(it);
            UIElement status = StatusMark(have);
            Border thumb = null;
            if (imgPath != null)
            {
                try
                {
                    System.Windows.Media.Imaging.BitmapImage bi = new System.Windows.Media.Imaging.BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = 320;
                    bi.UriSource = new Uri(imgPath);
                    bi.EndInit();
                    thumb = new Border();
                    thumb.Width = 118;
                    thumb.Height = 66;
                    thumb.CornerRadius = new CornerRadius(9);
                    thumb.ClipToBounds = true;
                    thumb.Margin = new Thickness(10, 0, 8, 0);
                    thumb.Cursor = Cursors.Hand;
                    thumb.ToolTip = Repo.T("ui.openImage");
                    Image im = new Image();
                    im.Source = bi;
                    im.Stretch = Stretch.UniformToFill;
                    thumb.Child = im;
                    thumb.MouseEnter += delegate { ShowHoverPreview(imgPath, string.IsNullOrEmpty(it.n) ? Repo.CatName(it.cat) + " #" + it.id : it.n); };
                thumb.MouseLeave += delegate { HideHoverPreview(); };
                thumb.MouseLeftButtonUp += delegate(object ts, MouseButtonEventArgs te) { te.Handled = true; ShowImageViewer(imgPath, string.IsNullOrEmpty(it.n) ? Repo.CatName(it.cat) + " #" + it.id : it.n); };
                }
                catch { thumb = null; }
            }
            if (thumb != null)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(thumb, 2);
                g.Children.Add(thumb);
                Grid.SetColumn(status, 3);
                g.Children.Add(status);
            }
            else
            {
                g.Children.Add(status);
            }
            row.Child = g;
            row.MouseEnter += delegate { row.Background = CPanelHi; row.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x47)); };
            row.MouseLeave += delegate { row.Background = CPanel; row.BorderBrush = CStroke; };
            return row;
        }

        void ToggleManual(ItemData it)
        {
            string key = Repo.ManualKey(it);
            bool now = Repo.IsCollected(it);
            Settings.Manual[key] = !now;
            Settings.Save();
            RebuildCurrentView();
        }

        void RebuildCurrentView()
        {
            if (view != "main") { UpdateChooser(); return; }
            BuildAll();
        }

        Canvas hoverLayer;
        Border hoverCard;

        void ShowHoverPreview(string path, string title)
        {
            Point p = Mouse.GetPosition(RootGrid);
            ShowHoverPreviewAt(path, title, p.X, p.Y);
        }

        void ShowHoverPreviewAt(string path, string title, double x, double y)
        {
            HideHoverPreview();
            if (RootGrid == null) return;
            if (hoverLayer == null)
            {
                hoverLayer = new Canvas();
                hoverLayer.IsHitTestVisible = false;
                hoverLayer.HorizontalAlignment = HorizontalAlignment.Stretch;
                hoverLayer.VerticalAlignment = VerticalAlignment.Stretch;
                Grid.SetRowSpan(hoverLayer, 3);
                Panel.SetZIndex(hoverLayer, 150);
                RootGrid.Children.Add(hoverLayer);
            }
            double maxW = Math.Max(360, RootGrid.ActualWidth - 80);
            double maxH = Math.Max(280, RootGrid.ActualHeight - 80);
            double w = Math.Min(960, maxW);
            double h = Math.Min(600, maxH);
            double left = x + 28;
            double top = y - h / 2;
            double maxX = RootGrid.ActualWidth - w - 12;
            double maxY = RootGrid.ActualHeight - h - 12;
            if (left > maxX) left = x - w - 28;
            if (left < 8) left = 8;
            if (top < 8) top = 8;
            if (top > maxY) top = maxY;

            Border card = new Border();
            card.Width = w;
            card.Height = h;
            card.Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x13));
            card.BorderBrush = CStroke;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(12);

            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            g.RowDefinitions.Add(new RowDefinition());
            TextBlock t = new TextBlock();
            t.Text = title;
            t.FontSize = 11;
            t.Foreground = CMuted;
            t.Margin = new Thickness(12, 0, 12, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            g.Children.Add(t);
            Image img = new Image();
            System.Windows.Media.Imaging.BitmapImage bi = new System.Windows.Media.Imaging.BitmapImage();
            bi.BeginInit();
            bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = 1600;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            img.Source = bi;
            img.Stretch = Stretch.Uniform;
            img.Margin = new Thickness(6);
            Grid.SetRow(img, 1);
            g.Children.Add(img);
            card.Child = g;
            Canvas.SetLeft(card, left);
            Canvas.SetTop(card, top);
            hoverLayer.Children.Add(card);
            hoverCard = card;
        }

        void HideHoverPreview()
        {
            if (hoverLayer != null) hoverLayer.Children.Clear();
            hoverCard = null;
        }

        Grid viewerOverlay;

        void ShowImageViewer(string path, string title)
        {
            HideHoverPreview();
            if (viewerOverlay != null) return;

            Grid overlay = new Grid();
            overlay.Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x07, 0x09, 0x0B));
            overlay.Focusable = true;
            overlay.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) CloseViewer(); };
            overlay.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (e.OriginalSource == overlay) CloseViewer(); };

            Border card = new Border();
            card.MaxWidth = 1120;
            card.MaxHeight = 820;
            card.Margin = new Thickness(48);
            card.Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x13));
            card.BorderBrush = CStroke;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(16);
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.VerticalAlignment = VerticalAlignment.Stretch;

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });

            Grid head = new Grid();
            TextBlock tt = new TextBlock();
            tt.Text = title;
            tt.FontSize = 13;
            tt.FontWeight = FontWeights.SemiBold;
            tt.Foreground = CText;
            tt.VerticalAlignment = VerticalAlignment.Center;
            tt.Margin = new Thickness(20, 0, 60, 0);
            tt.TextTrimming = TextTrimming.CharacterEllipsis;
            head.Children.Add(tt);

            Button x = new Button();
            x.Style = (Style)FindResource("WinBtn");
            x.Content = "\uE8BB";
            x.FontFamily = new FontFamily("Segoe MDL2 Assets");
            x.FontSize = 10;
            x.HorizontalAlignment = HorizontalAlignment.Right;
            x.Margin = new Thickness(0, 0, 8, 0);
            x.Click += delegate { CloseViewer(); };
            head.Children.Add(x);
            root.Children.Add(head);

            ScrollViewer sv = new ScrollViewer();
            sv.Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0C, 0x0E));
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Image img = new Image();
            System.Windows.Media.Imaging.BitmapImage bi = new System.Windows.Media.Imaging.BitmapImage();
            bi.BeginInit();
            bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = 1600;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            img.Source = bi;
            img.Stretch = Stretch.Uniform;
            img.Margin = new Thickness(6);
            sv.Content = img;
            Grid.SetRow(sv, 1);
            root.Children.Add(sv);

            bool actual = false;

            Grid foot = new Grid();
            foot.Margin = new Thickness(20, 0, 20, 0);
            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            ToggleButton fit = new ToggleButton();
            fit.Style = (Style)FindResource("ChipToggle");
            fit.Content = Repo.T("ui.viewerFit");
            fit.IsChecked = true;
            fit.Margin = new Thickness(0, 0, 4, 0);
            ToggleButton full = new ToggleButton();
            full.Style = (Style)FindResource("ChipToggle");
            full.Content = Repo.T("ui.viewerActual");
            fit.Click += delegate
            {
                actual = false;
                fit.IsChecked = true;
                full.IsChecked = false;
                img.Stretch = Stretch.Uniform;
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            };
            full.Click += delegate
            {
                actual = true;
                fit.IsChecked = false;
                full.IsChecked = true;
                img.Stretch = Stretch.None;
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            };
            left.Children.Add(fit);
            left.Children.Add(full);
            foot.Children.Add(left);

            Button open = new Button();
            open.Style = (Style)FindResource("GhostBtn");
            open.Content = Repo.T("ui.viewerOpen");
            open.Padding = new Thickness(14, 7, 14, 7);
            open.VerticalAlignment = VerticalAlignment.Center;
            open.HorizontalAlignment = HorizontalAlignment.Right;
            open.Margin = new Thickness(0, 0, 96, 0);
            open.Click += delegate { try { System.Diagnostics.Process.Start(path); } catch { } };
            foot.Children.Add(open);

            Button close = new Button();
            close.Style = (Style)FindResource("GhostBtn");
            close.Content = Repo.T("ui.aboutClose");
            close.Padding = new Thickness(18, 7, 18, 7);
            close.VerticalAlignment = VerticalAlignment.Center;
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Click += delegate { CloseViewer(); };
            foot.Children.Add(close);

            Grid.SetRow(foot, 2);
            root.Children.Add(foot);

            card.Child = root;
            overlay.Children.Add(card);
            Grid.SetRowSpan(overlay, 3);
            Panel.SetZIndex(overlay, 210);
            RootGrid.Children.Add(overlay);
            viewerOverlay = overlay;
            overlay.Focus();
        }

        void CloseViewer()
        {
            if (viewerOverlay != null)
            {
                RootGrid.Children.Remove(viewerOverlay);
                viewerOverlay = null;
            }
        }

        string ItemImagePath(ItemData it)
        {
            try
            {
                string dir = System.IO.Path.Combine(SaveIO.AppDir(), "images", Repo.CurrentGame.Id);
                string baseName = (it.cat == "nm" ? "page" : it.cat) + "_" + it.id;
                string[] exts = new string[] { ".jpg", ".jpeg", ".png" };
                foreach (string ext in exts)
                {
                    string p = System.IO.Path.Combine(dir, baseName + ext);
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }

        static Grid CatIcon(string cat, double size, Brush brush)
        {
            string d;
            switch (cat)
            {
                case "page":
                    d = "M6 3 H15 L19 7 V21 H6 Z M15 3 V7 H19 M9 11 H16 M9 14 H16 M9 17 H14";
                    break;
                case "nm":
                    d = "M6 3 H15 L19 7 V21 H6 Z M15 3 V7 H19 M9 14 L10.5 17 L13.5 17 L15 14 M11 18.5 L12 20 L13 18.5";
                    break;
                case "thermos":
                    d = "M9 3 H15 V5 H14 V8 H16 V20 A2 2 0 0 1 14 22 H10 A2 2 0 0 1 8 20 V8 H10 V5 H9 Z M10 12 H14";
                    break;
                case "chest":
                    d = "M3 9 H21 V19 H3 Z M3 9 L5 5 H19 L21 9 M3 13 H21 M11 11 H13 V15 H11 Z";
                    break;
                case "cans":
                    d = "M6 17 H10 V21 H6 Z M14 17 H18 V21 H14 Z M10 12 H14 V16 H10 Z M5 21 H19";
                    break;
                case "radio":
                    d = "M3 8 H21 V20 H3 Z M3 8 L15 3 M6 11 H10 V15 H6 Z M15 12 A3 3 0 1 0 15.1 12";
                    break;
                case "tv":
                    d = "M3 5 H21 V17 H3 Z M8 21 H16 M12 17 V21";
                    break;
                case "sign":
                    d = "M7 3 H17 V9 H7 Z M12 9 V21 M9 21 H15 M8 4.5 H16";
                    break;
                case "alarm":
                    d = "M12 8 A6 6 0 1 0 12.1 8 M12 8 V12 L15 13 M6 3 L3 6 M18 3 L21 6";
                    break;
                case "cutout":
                    d = "M10 3 H14 V6 H10 Z M8 6 H16 L17 21 H7 Z M10 10 H14";
                    break;
                case "game":
                    d = "M6 8 H18 A3 3 0 0 1 21 11 V17 A3 3 0 0 1 18 20 H12 L10 17 H6 A3 3 0 0 1 3 14 V11 A3 3 0 0 1 6 8 Z M9 11.5 V14.5 M7.5 13 H10.5 M16 12 A1 1 0 1 0 16.1 12 M18 14 A1 1 0 1 0 18.1 14";
                    break;
                default:
                    d = "M3 3 H10 V10 H3 Z M14 3 H21 V10 H14 Z M3 14 H10 V21 H3 Z M14 14 H21 V21 H14 Z";
                    break;
            }
            Path p = new Path();
            p.Data = Geometry.Parse(d);
            p.Stroke = brush;
            p.StrokeThickness = 1.5;
            p.StrokeStartLineCap = PenLineCap.Round;
            p.StrokeEndLineCap = PenLineCap.Round;
            p.StrokeLineJoin = PenLineJoin.Round;
            p.Fill = Brushes.Transparent;
            p.Stretch = Stretch.Uniform;
            p.SnapsToDevicePixels = true;
            Grid host = new Grid();
            host.Width = size;
            host.Height = size;
            host.VerticalAlignment = VerticalAlignment.Center;
            host.Children.Add(p);
            return host;
        }

        UIElement StatusMark(bool have)
        {
            Ellipse e = new Ellipse();
            e.Width = 8;
            e.Height = 8;
            e.Fill = have ? CGreen : CRed;
            e.Opacity = 1.0;
            e.VerticalAlignment = VerticalAlignment.Center;
            e.Margin = new Thickness(12, 0, 6, 0);
            return e;
        }

        /* ---------------- helpers ---------------- */

        Grid ProgressBar(double pct, double height, SolidColorBrush color)
        {
            Border back = new Border();
            back.Height = height;
            back.CornerRadius = new CornerRadius(height / 2);
            back.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x23, 0x28));
            back.HorizontalAlignment = HorizontalAlignment.Stretch;

            Border fill = new Border();
            fill.Height = height;
            fill.CornerRadius = new CornerRadius(height / 2);
            fill.Background = color;
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            fill.Width = 0;

            Grid host = new Grid();
            host.Children.Add(back);
            host.Children.Add(fill);
            host.SizeChanged += delegate
            {
                double w = host.ActualWidth * Math.Min(1, Math.Max(0, pct));
                DoubleAnimation an = new DoubleAnimation(fill.Width, w, TimeSpan.FromMilliseconds(450));
                an.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                fill.BeginAnimation(FrameworkElement.WidthProperty, an);
            };
            return host;
        }

        static Border Spacer(double h)
        {
            Border b = new Border();
            b.Height = h;
            return b;
        }

        Button ActionButton(string text, string styleKey)
        {
            Button b = new Button();
            b.Style = (Style)FindResource(styleKey);
            b.Content = text;
            return b;
        }

        void SetContent(UIElement el)
        {
            ContentHost.Child = el;
            ContentHost.Opacity = 0;
            ContentHost.RenderTransform = new TranslateTransform(0, 10);
            DoubleAnimation fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(220));
            ContentHost.BeginAnimation(UIElement.OpacityProperty, fade);
            DoubleAnimation slide = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260));
            slide.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            ((TranslateTransform)ContentHost.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slide);
        }

        void ToastMsg(string text)
        {
            ToastText.Text = text;
            Toast.Visibility = Visibility.Visible;
            DoubleAnimation an = new DoubleAnimation(1, TimeSpan.FromMilliseconds(150));
            Toast.BeginAnimation(UIElement.OpacityProperty, an);
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(3.2);
            timer.Tick += delegate
            {
                timer.Stop();
                DoubleAnimation outAn = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
                outAn.Completed += delegate { Toast.Visibility = Visibility.Collapsed; };
                Toast.BeginAnimation(UIElement.OpacityProperty, outAn);
            };
            timer.Start();
        }

        /* ---------------- actions ---------------- */

        void UpdateFromSave(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(Repo.ConfigPath))
            {
                PromptForSave();
                return;
            }
            SaveIO.BackupOncePerSession(Repo.ConfigPath);
            if (SaveIO.Load(Repo.ConfigPath))
            {
                BuildAll();
                StartSaveWatcher(Repo.ConfigPath);
                ToastMsg(Repo.T("ui.reload"));
            }
            else ToastMsg(Repo.LoadError == null ? "?" : Repo.LoadError);
        }

        void PromptForSave()
        {
            using (System.Windows.Forms.OpenFileDialog dlg = new System.Windows.Forms.OpenFileDialog())
            {
                dlg.Title = Repo.T("ui.chooseFile");
                dlg.Filter = "Alan Wake save (config)|config|All files|*.*";
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                SaveIO.BackupOncePerSession(dlg.FileName);
                if (SaveIO.Load(dlg.FileName))
                {
                    Settings.LastGame = Repo.CurrentGame.Id;
                    Settings.LastProfile = dlg.FileName;
                    Settings.Save();
                    view = "main";
                    currentCat = "overview";
                    SetSidebar(true);
                    BuildAll();
                    StartSaveWatcher(dlg.FileName);
                    ToastMsg(Repo.T("ui.reload"));
                }
                else { EnterManual(dlg.FileName, null); return; }
                Repo.ManualMode = false;
            }
        }

        void DoBackup()
        {
            if (string.IsNullOrEmpty(Repo.ConfigPath)) { PromptForSave(); return; }
            try
            {
                string dest = SaveIO.Backup(Repo.ConfigPath);
                ToastMsg(Repo.T("ui.backupOk").Replace("{path}", dest));
            }
            catch (Exception ex)
            {
                ToastMsg(Repo.T("ui.backupErr").Replace("{msg}", ex.Message));
            }
        }

        void CopyMissing(string cat)
        {
            StringBuilder sb = new StringBuilder();
            List<ItemData> items = Repo.ByCat(cat);
            for (int i = 0; i < items.Count; i++)
            {
                ItemData it = items[i];
                if (Repo.IsCollected(it)) continue;
                sb.AppendLine("- " + Repo.EpName(it.ep) + " · " + Repo.SecName(it) + " · " +
                              (string.IsNullOrEmpty(it.n) ? Repo.CatName(it.cat) + " #" + it.id : it.n));
                sb.AppendLine("  " + it.Loc(Repo.Lang));
            }
            if (sb.Length == 0) { ToastMsg(Repo.T("ui.nothingLeft")); return; }
            try
            {
                Clipboard.SetText(sb.ToString());
                ToastMsg(Repo.T("ui.copied"));
            }
            catch (Exception ex) { ToastMsg(ex.Message); }
        }

        /* ---------------- window chrome ---------------- */

        void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        void MaxBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        void RoundCorners()
        {
            try
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int pref = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref pref, sizeof(int));
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void MinBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
