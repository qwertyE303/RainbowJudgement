using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityModManagerNet;

namespace RainbowJudgement
{
    /// <summary>Mod 入口：加载补丁、装配设置界面、处理开关/卸载</summary>
    public static class Main
    {
        public static RainbowSettings Settings;
        public static string ModPath;
        public static bool Enabled { get; private set; }

        /// <summary>
        /// 所有补丁的统一总闸门：Mod 已加载 **且** 玩家开着「开启彩虹判定」。
        /// 收敛原先散落在各 hook 里的 `!Main.Enabled || !Main.Settings.EnableRainbow`
        /// （写成属性顺带把 Settings 的空引用也兜住了）。语义与逐处判断完全一致。
        /// </summary>
        public static bool Active
        {
            get
            {
                if (!Enabled) return false;
                RainbowSettings settings = Settings;
                return settings != null && settings.EnableRainbow;
            }
        }

        private static Harmony _harmony;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Settings = UnityModManager.ModSettings.Load<RainbowSettings>(modEntry);
            ModPath = modEntry.Path;
            Logger.Init(); // 定位 Mod 目录并清空 <RJ Mod 目录>\Log.txt（每次启动游戏重记）

            CustomJudge.EnsureDefaults(); // 首次运行：写入出厂四条自定义判定
            CustomJudge.InvalidateLayout();

            if (!Compat.SetAccessors())
                Logger.Warn("[Main] 当前判定格访问器解析失败（tick 取色/自动判定识别会退化）");

            _harmony = new Harmony(modEntry.Info.Id);
            PatchAllIndividually();

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = SettingsGui.Draw;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUnload = OnUnload;

            Enabled = true;
            RainbowProgress.Clear();
            Logger.Guard("Main/Load", delegate { LiveDisplay.EnsureUI(); });
            Logger.Guard("Main/Load", delegate
            {
                // ⚠ 启动期的任何一行日志都【不能】调用 Lang.Resolve() / Lang.T() / Lang.DetectFromGame()：
                // 它们会去读游戏的 Persistence.language，而 2.9.8 的 PlayerPrefsJson 在"存档还没被读出来"
                // 时被访问，会先造一份空存档注册进静态表（Select → CreateSaveFile），且它的 AddSaveFile
                // 不替换已有条目 → 游戏随后真正读出来的 data.sav 被静默丢弃、并被写回成空档，
                // 玩家的进度与设置就会被清空。详见 Lang 的类注释（这里原本就是那起事故的触发点）。
                Logger.Log("[Main] 语言预选延迟：启动期不访问游戏 Persistence（2.9.8 会在存档读完前造出空档并覆盖玩家存档）");
                Logger.Log("[Main] UMM ModPath=" + ModPath + " | 资源基准目录=" + ModPaths.BaseDir
                    + " | 日志文件=" + Logger.FilePath
                    + " | 语言设置=" + Settings.GuiLanguage + "（0 = 未设置，按游戏语言预选，延迟解析）"
                    + " | 开关 彩虹=" + Settings.EnableRainbow + " 判定详情=" + Settings.ShowJudgeDetails
                    + "（时间=" + Settings.ShowAverageTime + " 颜色=" + Settings.ShowAverageColor
                    + " 角度=" + Settings.ShowAverageAngle + "）"
                    + " | 实时 颜色=" + Settings.ShowLiveColor + " 时间=" + Settings.ShowLiveTime
                    + " 角度=" + Settings.ShowLiveAngle
                    + " | 自定义判定=" + Settings.EnableCustomJudge + " 档位数=" + CustomJudge.EnabledCount
                    + " 显示计数=" + Settings.ShowCustomCount + " 结尾页计数=" + Settings.ShowCustomCountInResults
                    + " | DebugLog=" + Settings.DebugLog);
            });
            return true;
        }

        /// <summary>
        /// 逐个注册补丁（**2.9.8 专属做法**）：不用 PatchAll。
        /// PatchAll 的语义是"任何一条补丁的目标类型/方法在当前游戏里不存在 → 抛异常 → 整个 Mod 加载失败"，
        /// 而 2.9.8 与 3.3.0 的游戏类型形状差异很大（例如这一版根本没有 scrMarginTracker）。
        /// 逐个注册 + try/catch 之后，缺哪一条只丢那一项功能，其余照常工作，并且失败原因会写进日志。
        /// </summary>
        private static void PatchAllIndividually()
        {
            List<System.Type> patches = new List<System.Type>();
            patches.Add(typeof(JudgeHooks.TickColorHook));
            patches.Add(typeof(JudgeHooks.TickAngleCrossCheckHook));
            patches.Add(typeof(JudgeHooks.GetMarginHook));
            patches.Add(typeof(JudgeHooks.MistakesAddHitHook));
            patches.Add(typeof(ProgressHooks.SceneAwakePatch));
            patches.Add(typeof(ProgressHooks.MistakesRevertPatch));
            patches.Add(typeof(ProgressHooks.MistakesResetPatch));
            patches.Add(typeof(ProgressHooks.SavedProgressStorePatch));
            patches.Add(typeof(ProgressHooks.CheckpointLoadPatch));
            patches.Add(typeof(ProgressHooks.SavedProgressDeletePatch));
            patches.Add(typeof(VisualHooks.HitTextColorPatch));
            patches.Add(typeof(VisualHooks.FlawlessXPatch));
            patches.Add(typeof(VisualHooks.FlawlessXResetPatch));
            patches.Add(typeof(MeterVisualPatch));
            patches.Add(typeof(ResultsPatch));
            patches.Add(typeof(LiveDisplay.EditorHidePatch));

            int ok = 0;
            for (int i = 0; i < patches.Count; i++)
            {
                System.Type type = patches[i];
                try
                {
                    _harmony.CreateClassProcessor(type).Patch();
                    ok++;
                }
                catch (Exception ex)
                {
                    Logger.Warn("[Main] 补丁注册失败（该项功能不可用）：" + type.Name + " → " + ex.Message);
                }
            }
            Logger.Log("[Main] 补丁注册完成：" + ok + "/" + patches.Count);
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            if (!value)
            {
                Settings.ShowJudgeDetails = false;
                Logger.Guard("Main/Toggle", delegate { MeterVisualPatch.RestoreAllMeters(); });
            }
            else
            {
                // 关闭期间游戏仍在计数 → 重新对齐（缺数据的条目用占位补齐，保证索引不错位）
                Logger.Guard("Main/Toggle", delegate { RainbowProgress.RealignFromGame(); });
                Logger.Guard("Main/Toggle", delegate { MeterVisualPatch.RefreshAllMeters(); });
            }
            return true;
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            Logger.Guard("Main/Unload", delegate
            {
                Enabled = false;
                MeterVisualPatch.RestoreAllMeters();
                LiveDisplay.HideAll();
                if (_harmony != null)
                {
                    _harmony.UnpatchAll(modEntry.Info.Id);
                    _harmony = null;
                }
            });
            return true;
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
        }
    }
}
