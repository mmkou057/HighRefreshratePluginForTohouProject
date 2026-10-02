// PluginSwitch.cs - 东方 Project 综合插件注入器 (C# 5, WinForms, x86)
//
// 同时管理三个 DLL 代理插件，部署/卸载以"改名备份"进行，不丢用户配置：
//   高刷新率插件  d3d9.dll + hfr.ini      （载荷 plugins\hfr\d3d9.dll）
//   全向移动插件  dinput8.dll + padhook.ini（载荷 plugins\omni\dinput8.dll）
//   轨迹可视化    traj.dll + traj.ini      （载荷 plugins\traj\traj.dll，调试）
//
// 用法：与本目录 plugins\ 一起放在项目根的 injector\ 下：
//   1) 下拉框选游戏（自动扫描 ..\test 与 ..\Tohou\STG 下所有 th*.exe）
//   2) 勾选/取消插件 → 立即应用（游戏运行中会提示自动关闭）
//      勾选 = 复制载荷/还原 .off 备份 + 补写默认 ini（已有 ini 不覆盖）
//      取消 = 游戏目录内改名 *.off（用户改过的按钮映射/刷新率保留）
//   3) [启动游戏] / [打开游戏目录]
//
// 兼容：hfr 的 d3d9.dll 对 TH08~TH20 做签名自动探测；dinput8.dll 通用于
//       DirectInput8 读键盘的东方作品。按钮/刷新率参数在游戏目录 ini 校准。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PluginSwitch
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly ComboBox _games;
        private readonly CheckBox _chkHfr;
        private readonly CheckBox _chkOm;
        private readonly CheckBox _chkTraj;
        private readonly Button _browse;
        private readonly Button _launch;
        private readonly Button _open;
        private readonly Label _status;
        private bool _suppress;

        /* 插件文件清单：[0]=代理 dll，[1]=配置 ini（日志名由 ini 推导） */
        private static readonly string[] HfrFiles = { "d3d9.dll", "hfr.ini" };
        private static readonly string[] OmFiles  = { "dinput8.dll", "padhook.ini" };
        private static readonly string[] TrajFiles = { "traj.dll", "traj.ini" };
        private const string OffSuffix = ".off";

        public MainForm()
        {
            Text = "东方 Project 插件管理器（高刷 + 全向移动）";
            Width = 680;
            Height = 360;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = SystemColors.Window;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            var lblGame = new Label { Text = "游戏:", Left = 12, Top = 18, AutoSize = true };
            _games = new ComboBox {
                Left = 60, Top = 14, Width = 490,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _games.SelectedIndexChanged += delegate { SaveCfg(); UpdateStatus(); };

            _browse = new Button { Text = "浏览...", Left = 560, Top = 13, Width = 96 };
            _browse.Click += Browse_Click;

            _chkHfr = new CheckBox {
                Text = "高刷新率插件  d3d9.dll —— 突破 60FPS（TH08~TH20 签名自动探测）",
                Left = 12, Top = 54, Width = 650, AutoSize = false
            };
            _chkHfr.CheckedChanged += Chk_CheckedChanged;

            _chkOm = new CheckBox {
                Text = "全向移动插件  dinput8.dll —— 手柄摇杆任意角度匀速移动（帧率自适应）",
                Left = 12, Top = 84, Width = 650, AutoSize = false
            };
            _chkOm.CheckedChanged += Chk_CheckedChanged;

            _chkTraj = new CheckBox {
                Text = "轨迹可视化（调试）  traj.dll —— 独立插件，[启动游戏]时自动注入；可与全向移动叠加对比",
                Left = 12, Top = 114, Width = 650, AutoSize = false
            };
            _chkTraj.CheckedChanged += Chk_CheckedChanged;

            _launch = new Button {
                Text = "启动游戏", Left = 12, Top = 152, Width = 130, Height = 36
            };
            _launch.Click += Launch_Click;

            _open = new Button {
                Text = "打开游戏目录", Left = 152, Top = 152, Width = 130, Height = 36
            };
            _open.Click += Open_Click;

            _status = new Label {
                Left = 12, Top = 202, Width = 644, Height = 140,
                AutoSize = false, BorderStyle = BorderStyle.FixedSingle
            };

            Controls.AddRange(new Control[] {
                lblGame, _games, _browse, _chkHfr, _chkOm, _chkTraj, _launch, _open, _status });

            Load += delegate { ScanGames(); UpdateStatus(); };
        }

        // ---------- 扫描与记忆 ----------

        /* 扫描 ..\test 与 ..\Tohou\STG 下所有主程序 th*.exe */
        private static string ProjectRoot
        {
            get { return Path.GetFullPath(Path.Combine(Application.StartupPath, "..")); }
        }

        private void ScanGames()
        {
            _games.Items.Clear();
            string[] roots = new[] {
                Path.Combine(ProjectRoot, "test"),
                Path.Combine(ProjectRoot, "Tohou", "STG")
            };
            var found = new List<string>();
            foreach (string root in roots) {
                if (!Directory.Exists(root)) continue;
                try {
                    string[] exes = Directory.GetFiles(root, "*.exe", SearchOption.AllDirectories);
                    foreach (string exe in exes) {
                        string low = (Path.GetFileName(exe) ?? "").ToLowerInvariant();
                        /* 只保留主程序 th*.exe, 跳过 custom/replay/score/update/setup 等 */
                        if (low.StartsWith("th") && low.EndsWith(".exe") &&
                            !low.Contains("custom") && !low.Contains("replay") &&
                            !low.Contains("score") && !low.Contains("update") &&
                            !low.Contains("setup") && !low.Contains("unins") &&
                            !low.Contains("prac") && !low.Contains("vpatch") &&
                            !low.Contains("converter") &&
                            !found.Exists(s => string.Equals(s, exe, StringComparison.OrdinalIgnoreCase))) {
                            found.Add(exe);
                        }
                    }
                } catch (Exception) { }
            }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string s in found) _games.Items.Add(s);

            /* 追加浏览历史（不在扫描根下的） */
            foreach (string s in ReadBrowsed()) {
                if (File.Exists(s) && !found.Exists(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase)))
                    _games.Items.Add(s);
            }

            /* 恢复上次选择 */
            string last = ReadLastGame();
            if (!string.IsNullOrEmpty(last) && _games.Items.Contains(last)) {
                _games.SelectedItem = last;
            } else if (_games.Items.Count > 0) {
                _games.SelectedIndex = 0;
            }
        }

        private static string CfgPath
        {
            get { return Path.Combine(Application.StartupPath, "PluginSwitch.ini"); }
        }

        private static Dictionary<string, string> ReadCfg()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try {
                if (!File.Exists(CfgPath)) return d;
                foreach (string line in File.ReadAllLines(CfgPath)) {
                    int eq = line.IndexOf('=');
                    if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            } catch (Exception) { }
            return d;
        }

        private void SaveCfg()
        {
            try {
                var sb = new StringBuilder();
                sb.AppendLine("[main]");
                string game = _games.SelectedItem as string;
                if (!string.IsNullOrEmpty(game)) sb.AppendLine("game=" + game);
                string br = ReadCfg().ContainsKey("browsed") ? ReadCfg()["browsed"] : "";
                if (!string.IsNullOrEmpty(br)) sb.AppendLine("browsed=" + br);
                File.WriteAllText(CfgPath, sb.ToString(), new UTF8Encoding(false));
            } catch (Exception) { }
        }

        private static string ReadLastGame()
        {
            var d = ReadCfg();
            return d.ContainsKey("game") ? d["game"] : null;
        }

        private static string[] ReadBrowsed()
        {
            var d = ReadCfg();
            if (!d.ContainsKey("browsed")) return new string[0];
            return d["browsed"].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static void AppendBrowsed(string path)
        {
            var d = ReadCfg();
            string existing = d.ContainsKey("browsed") ? d["browsed"] : "";
            var list = new List<string>(existing.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
            string up = path.ToUpperInvariant();
            if (!list.Exists(s => s.ToUpperInvariant() == up)) list.Add(path);
            try {
                var sb = new StringBuilder();
                sb.AppendLine("[main]");
                if (d.ContainsKey("game")) sb.AppendLine("game=" + d["game"]);
                sb.AppendLine("browsed=" + string.Join("|", list));
                File.WriteAllText(CfgPath, sb.ToString(), new UTF8Encoding(false));
            } catch (Exception) { }
        }

        private void Browse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog {
                Filter = "游戏可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                Title = "选择游戏 .exe"
            }) {
                if (dlg.ShowDialog() == DialogResult.OK) {
                    if (!_games.Items.Contains(dlg.FileName)) _games.Items.Add(dlg.FileName);
                    AppendBrowsed(dlg.FileName);
                    _games.SelectedItem = dlg.FileName;
                    UpdateStatus();
                }
            }
        }

        private void Open_Click(object sender, EventArgs e)
        {
            string gamePath = _games.SelectedItem as string;
            if (string.IsNullOrEmpty(gamePath)) return;
            string dir = Path.GetDirectoryName(gamePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Process.Start("explorer.exe", "\"" + dir + "\"");
        }

        // ---------- 部署核心 ----------

        /* 插件状态：1=已启用（游戏目录有活动文件） 0=已停用（仅 .off 备份） -1=未安装 */
        private static int PluginState(string gameDir, string[] files)
        {
            bool act = false, off = false;
            foreach (string f in files) {
                if (File.Exists(Path.Combine(gameDir, f))) act = true;
                if (File.Exists(Path.Combine(gameDir, f + OffSuffix))) off = true;
            }
            return act ? 1 : (off ? 0 : -1);
        }

        private static string LogPath(string gameDir, string iniFile)
        {
            return Path.Combine(gameDir,
                Path.GetFileNameWithoutExtension(iniFile) + ".log");
        }

        /* 启用：还原 .off 或释放载荷；ini 缺失时补写默认（用户校准过的 ini 永不覆盖） */
        private static string EnablePlugin(string gameDir, string[] files,
                                           string payloadGroup)
        {
            string dll = files[0], ini = files[1];
            string tDll = Path.Combine(gameDir, dll);
            string tIni = Path.Combine(gameDir, ini);
            try {
                if (!File.Exists(tDll)) {
                    string bak = tDll + OffSuffix;
                    if (File.Exists(bak)) {
                        File.Move(bak, tDll);
                    } else {
                        string payload = Path.Combine(Application.StartupPath,
                            "plugins", payloadGroup, dll);
                        if (!File.Exists(payload))
                            return "缺少载荷文件 plugins\\" + payloadGroup + "\\" + dll;
                        File.Copy(payload, tDll, true);
                    }
                }
                if (!File.Exists(tIni)) {
                    string bakIni = tIni + OffSuffix;
                    if (File.Exists(bakIni)) File.Move(bakIni, tIni);
                    else if (ini == "hfr.ini") WriteHfrIni(gameDir);
                    else if (ini == "traj.ini") WriteTrajIni(gameDir);
                    else WritePadhookIni(gameDir);
                }
                string log = LogPath(gameDir, ini);
                if (File.Exists(log)) File.Delete(log);   /* 清旧日志便于诊断 */
                return null;
            } catch (Exception ex) {
                return ex.Message;
            }
        }

        /* 停用：游戏目录内改名 *.off，文件与用户配置都保留，可随时还原 */
        private static string DisablePlugin(string gameDir, string[] files)
        {
            try {
                foreach (string f in files) {
                    string t = Path.Combine(gameDir, f);
                    if (!File.Exists(t)) continue;
                    string bak = t + OffSuffix;
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(t, bak);
                }
                string log = LogPath(gameDir, files[1]);
                if (File.Exists(log)) File.Delete(log);
                return null;
            } catch (Exception ex) {
                return ex.Message;
            }
        }

        /* 写 hfr.ini：无 BOM UTF-8，默认按显示器刷新率 + 子步长全开 */
        private static void WriteHfrIni(string gameDir)
        {
            string ini =
                "; hfr 高刷插件配置 —— 与 d3d9.dll 一同放在游戏 exe 同目录\r\n" +
                "; TH08~TH20 自动探测签名，不限制 exe 版本；其它作品仅透明代理\r\n" +
                "[hfr]\r\n" +
                "; 目标刷新率：0 = 跟随显示器；>0 = 强制指定（如 120/144/240）\r\n" +
                "fps=0\r\n" +
                "; 垂直同步：1 = Present 间隔一（推荐）；0 = 插件软件限帧\r\n" +
                "vsync=1\r\n" +
                "; 子步长解耦：1 = 弹幕/自机/道具按显示率子步进（真正高刷平滑）\r\n" +
                "substep=1\r\n" +
                "log=1\r\n" +
                "; 独占全屏请求的刷新率（Hz），0 = 自动取显示器当前刷新率\r\n" +
                "fullscreen_refresh=0\r\n" +
                "debug=0\r\n" +
                "; 子 tick 输入：1 = 方向/focus 按显示率采样（高刷下更跟手）\r\n" +
                "subtick_input=1\r\n" +
                "; 敌机/精灵视觉插值（纯视觉，不动判定）\r\n" +
                "enemy_interp=1\r\n" +
                "; 擦弹表现：子弹着色抖动 + 自机辉光（纯视觉）\r\n" +
                "graze_bullets=1\r\n" +
                "graze_glow=1\r\n";
            File.WriteAllText(Path.Combine(gameDir, "hfr.ini"), ini, new UTF8Encoding(false));
        }

        /* 写 padhook.ini：无 BOM UTF-8，mode=4 + 默认按钮映射（可事后校准） */
        private static void WritePadhookIni(string gameDir)
        {
            string ini =
                "[padhook]\r\n" +
                "mode=4\r\n" +
                "btn_shoot=0\r\n" +
                "btn_bomb=1\r\n" +
                "btn_slow=4\r\n" +
                "btn_pause=7\r\n";
            File.WriteAllText(Path.Combine(gameDir, "padhook.ini"), ini, new UTF8Encoding(false));
        }

        /* 写 traj.ini：无 BOM UTF-8，轨迹可视化默认配置 */
        private static void WriteTrajIni(string gameDir)
        {
            string ini =
                "[traj]\r\n" +
                "enabled=1\r\n" +
                "; 轨迹点数（默认 300 = 5 秒 @60Hz）\r\n" +
                "trail_len=300\r\n" +
                "; 蓝线 stick tip 半径（playfield 单位，默认 24）\r\n" +
                "stick_scale=24\r\n";
            File.WriteAllText(Path.Combine(gameDir, "traj.ini"), ini, new UTF8Encoding(false));
        }

        /* 找出正在运行的该游戏进程（本程序 x86，可读 32 位游戏模块路径） */
        private static List<Process> FindRunningGames(string gamePath)
        {
            var list = new List<Process>();
            foreach (Process p in Process.GetProcesses()) {
                try {
                    if (p.MainModule != null &&
                        string.Equals(p.MainModule.FileName, gamePath, StringComparison.OrdinalIgnoreCase)) {
                        list.Add(p);
                    }
                } catch { }
            }
            return list;
        }

        /* 切换前确保游戏已关闭；运行中提示可自动关闭. 返回 false=用户取消 */
        private static bool EnsureGameClosed(string gamePath)
        {
            List<Process> running = FindRunningGames(gamePath);
            if (running.Count == 0) return true;
            DialogResult r = MessageBox.Show(
                "游戏正在运行，切换插件必须先关闭游戏。\n是否自动关闭游戏？",
                "需要关闭游戏", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return false;
            foreach (Process p in running) {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
            System.Threading.Thread.Sleep(400);
            return true;
        }

        /* 勾选变化 → 把三个插件都应用到当前勾选状态（幂等，三插件互相独立） */
        private void Chk_CheckedChanged(object sender, EventArgs e)
        {
            if (_suppress) return;
            string gamePath = _games.SelectedItem as string;
            if (string.IsNullOrEmpty(gamePath)) return;
            string gameDir = Path.GetDirectoryName(gamePath);
            if (string.IsNullOrEmpty(gameDir)) return;

            if (!EnsureGameClosed(gamePath)) { UpdateStatus(); return; }

            string err = Apply(HfrFiles, "hfr", _chkHfr.Checked, gameDir);
            if (err == null) err = Apply(OmFiles, "omni", _chkOm.Checked, gameDir);
            if (err == null) err = Apply(TrajFiles, "traj", _chkTraj.Checked, gameDir);
            if (err != null)
                MessageBox.Show("切换失败: " + err, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateStatus();
        }

        private static string Apply(string[] files, string group, bool enable, string gameDir)
        {
            int st = PluginState(gameDir, files);
            if (enable) {
                if (st == 1) return null;          /* 已启用 */
                return EnablePlugin(gameDir, files, group);
            }
            if (st != 1) return null;              /* 本来就没启用 */
            return DisablePlugin(gameDir, files);
        }

        private void Launch_Click(object sender, EventArgs e)
        {
            string gamePath = _games.SelectedItem as string;
            if (string.IsNullOrEmpty(gamePath) || !File.Exists(gamePath)) {
                MessageBox.Show("请先选择有效的游戏 .exe",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try {
                Process p = Process.Start(new ProcessStartInfo {
                    FileName = gamePath,
                    WorkingDirectory = Path.GetDirectoryName(gamePath) ?? "",
                    UseShellExecute = false
                });
                /* 轨迹插件是独立 DLL，不由游戏自动加载，需远程注入 */
                string gameDir = Path.GetDirectoryName(gamePath) ?? "";
                string trajDll = Path.Combine(gameDir, "traj.dll");
                if (p != null && File.Exists(trajDll)) {
                    System.Threading.Thread.Sleep(1200);  /* 等进程初始化 */
                    string err = InjectDll(p, trajDll);
                    if (err != null)
                        MessageBox.Show("traj.dll 注入失败: " + err,
                            "轨迹插件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            } catch (Exception ex) {
                MessageBox.Show("启动失败: " + ex.Message,
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------- DLL 远程注入（轨迹插件用） ----------

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr addr,
            uint size, uint type, uint protect);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr addr,
            byte[] buf, uint size, out uint written);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string name);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetProcAddress(IntPtr mod, string name);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr attr,
            uint stack, IntPtr start, IntPtr param, uint flags, out uint tid);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr addr,
            uint size, uint type);

        private static string InjectDll(Process p, string dllPath)
        {
            const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000,
                       PAGE_READWRITE = 4, MEM_RELEASE = 0x8000;
            try {
                /* LoadLibraryA 需要 ANSI 路径（系统代码页，支持中文目录） */
                byte[] pathBytes = Encoding.Default.GetBytes(dllPath + "\0");
                IntPtr mem = VirtualAllocEx(p.Handle, IntPtr.Zero,
                    (uint)pathBytes.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (mem == IntPtr.Zero) return "VirtualAllocEx 失败";
                uint written;
                if (!WriteProcessMemory(p.Handle, mem, pathBytes,
                        (uint)pathBytes.Length, out written))
                    return "WriteProcessMemory 失败";
                IntPtr loadLib = GetProcAddress(GetModuleHandle("kernel32.dll"),
                    "LoadLibraryA");
                if (loadLib == IntPtr.Zero) return "找不到 LoadLibraryA";
                uint tid;
                IntPtr th = CreateRemoteThread(p.Handle, IntPtr.Zero, 0,
                    loadLib, mem, 0, out tid);
                if (th == IntPtr.Zero) return "CreateRemoteThread 失败";
                WaitForSingleObject(th, 5000);
                VirtualFreeEx(p.Handle, mem, 0, MEM_RELEASE);
                return null;
            } catch (Exception ex) {
                return ex.Message;
            }
        }

        // ---------- 状态显示 ----------

        private static string StateText(int st)
        {
            if (st == 1) return "● 已启用";
            if (st == 0) return "○ 已停用（.off 备份保留，可还原）";
            return "－ 未安装";
        }

        private static string PayloadText(string group, string dll)
        {
            string p = Path.Combine(Application.StartupPath, "plugins", group, dll);
            return File.Exists(p)
                ? string.Format("载荷 plugins\\{0}\\{1} 就绪", group, dll)
                : string.Format("!! 缺少载荷 plugins\\{0}\\{1}", group, dll);
        }

        private void UpdateStatus()
        {
            string gamePath = _games.SelectedItem as string;
            if (string.IsNullOrEmpty(gamePath)) {
                _status.Text = "未选择游戏.\n请用 [浏览...] 选择 .exe, 或确认 ..\\test 目录存在.";
                SetChecks(false, false, false, false);
                return;
            }
            string gameDir = Path.GetDirectoryName(gamePath) ?? "";
            bool running = FindRunningGames(gamePath).Count > 0;
            int hs = PluginState(gameDir, HfrFiles);
            int os = PluginState(gameDir, OmFiles);
            int ts = PluginState(gameDir, TrajFiles);

            SetChecks(hs == 1, os == 1, ts == 1, true);
            _status.Text =
                "游戏: " + gamePath + (running ? "   [运行中]" : "") + "\n" +
                "高刷: " + StateText(hs) +
                "      全向: " + StateText(os) +
                "      轨迹: " + StateText(ts) + "\n" +
                PayloadText("hfr", "d3d9.dll") + "    " +
                PayloadText("omni", "dinput8.dll") + "    " +
                PayloadText("traj", "traj.dll") + "\n" +
                "提示: 勾选即应用（自动关闭运行中的游戏）；停用 = 改名 .off 不丢配置。\n" +
                "      按钮编号/刷新率参数在游戏目录 padhook.ini / hfr.ini 里校准。";
        }

        /* 同步勾选框到实际状态；disabled=无游戏可选时禁用勾选 */
        private void SetChecks(bool hfr, bool om, bool traj, bool enabled)
        {
            _suppress = true;
            _chkHfr.Checked = hfr;
            _chkOm.Checked = om;
            _chkTraj.Checked = traj;
            _chkHfr.Enabled = enabled;
            _chkOm.Enabled = enabled;
            _chkTraj.Enabled = enabled;
            _suppress = false;
        }
    }
}
