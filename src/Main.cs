using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace RainbowJudgement
{
    public static class Main
    {
        public static RainbowSettings Settings;
        public static string ModPath;
        public static bool Enabled { get; private set; }

        private static Harmony _harmony;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Settings = UnityModManager.ModSettings.Load<RainbowSettings>(modEntry);
            ModPath = modEntry.Path;

            _harmony = new Harmony(modEntry.Info.Id);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUnload = OnUnload;

            Enabled = true;
            RainbowState.Reset();
            try { RainbowCounter.EnsureUI(); } catch (Exception ex) { Logger.Log("[Main/Load] 计数器UI提前创建失败: " + ex.Message); }

            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            RainbowState.Reset();
            if (!Enabled)
            {
                Settings.ShowAverageJudgment = false;
                try { MeterVisualPatch.RestoreAllMeters(); } catch { }
            }
            else
            {
                try { MeterVisualPatch.RefreshAllMeters(); } catch { }
            }
            return true;
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            try
            {
                Enabled = false;
                MeterVisualPatch.RestoreAllMeters();
                if (_harmony != null)
                {
                    try { _harmony.UnpatchAll(modEntry.Info.Id); } catch { }
                    _harmony = null;
                }
            }
            catch { }
            return true;
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.BeginVertical("box", new GUILayoutOption[0]);
            GUILayout.Space(5f);

            GUIStyle title = new GUIStyle(GUI.skin.label);
            title.fontSize = 20;
            title.fontStyle = FontStyle.Bold;
            title.normal.textColor = new Color(0.55f, 0.75f, 1f);
            title.alignment = TextAnchor.MiddleCenter;
            GUILayout.Label("Rainbow Judgement", title, new GUILayoutOption[0]);
            GUILayout.Space(3f);


            // Level 1: Enable Rainbow Judgment
            bool enable = GUILayout.Toggle(Settings.EnableRainbow,
                "Enable Rainbow Judgment", new GUILayoutOption[0]);
            if (enable != Settings.EnableRainbow)
            {
                Settings.EnableRainbow = enable;
                RainbowState.Reset();
                if (enable)
                {
                    try { MeterVisualPatch.RefreshAllMeters(); } catch { }
                }
                else
                {
                    Settings.ShowAverageJudgment = false;
                    try { MeterVisualPatch.RestoreAllMeters(); } catch { }
                }
            }

            bool guiEnabled = Settings.EnableRainbow;

            // Level 2: Show Average Judgment
            GUI.enabled = guiEnabled;
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(20f);
            bool showAvg = GUILayout.Toggle(Settings.ShowAverageJudgment,
                "Show Average Judgment", new GUILayoutOption[0]);
            GUILayout.EndHorizontal();
            if (showAvg != Settings.ShowAverageJudgment)
            {
                Settings.ShowAverageJudgment = showAvg;
                if (!showAvg) { Settings.ShowAverageTime = false; Settings.ShowAverageColor = false; }
            }

            // Level 3: time / color sub-options
            GUI.enabled = guiEnabled && Settings.ShowAverageJudgment;
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(40f);
            bool showTime = GUILayout.Toggle(Settings.ShowAverageTime,
                "Show Average Absolute Deviation", new GUILayoutOption[0]);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(40f);
            bool showColor = GUILayout.Toggle(Settings.ShowAverageColor,
                "Show Average Judgment Color", new GUILayoutOption[0]);
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            if (showTime != Settings.ShowAverageTime) Settings.ShowAverageTime = showTime;
            if (showColor != Settings.ShowAverageColor) Settings.ShowAverageColor = showColor;

            // Level 2: Show Rainbow Counter
            GUI.enabled = guiEnabled;
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(20f);
            bool counter = GUILayout.Toggle(Settings.ShowRainbowCounter,
                "Show Rainbow Counter", new GUILayoutOption[0]);
            GUILayout.EndHorizontal();
            if (counter != Settings.ShowRainbowCounter) Settings.ShowRainbowCounter = counter;

            // Level 3: counter settings (font size / X / Y)
            GUI.enabled = guiEnabled && Settings.ShowRainbowCounter;
            if (_fsStr == "") _fsStr = Settings.CounterFontSize.ToString();
            if (_xStr == "") _xStr = Settings.CounterX.ToString();
            if (_yStr == "") _yStr = Settings.CounterY.ToString();
            if (_spStr == "") _spStr = Settings.CounterSpacing.ToString();
            CounterSlider("Font Size", ref Settings.CounterFontSize, 10, 200, ref _fsStr, "fsField");
            CounterSlider("X Position", ref Settings.CounterX, -2000, 2000, ref _xStr, "xField");
            CounterSlider("Y Position", ref Settings.CounterY, -1000, 1000, ref _yStr, "yField");
            CounterSlider("Spacing", ref Settings.CounterSpacing, 0, 20, ref _spStr, "spField");
            GUI.enabled = true;

            // Debug log (kept for development)
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(20f);
            bool debug = GUILayout.Toggle(Settings.DebugLog,
                "Debug Log", new GUILayoutOption[0]);
            GUILayout.EndHorizontal();
            Settings.DebugLog = debug;

            GUILayout.Space(5f);
            GUILayout.EndVertical();
        }

        /// <summary>滑动条+数值框同步调整（拖动滑条→框更新；输入框→滑条更新）</summary>
                private static string _fsStr = "";
        private static string _xStr = "";
        private static string _yStr = "";
        private static string _spStr = "";

        /// <summary>滑动条+数值框同步（焦点控制：框有焦点时输入生效，无焦点时滑块生效）</summary>
        private static void CounterSlider(string label, ref int value, int min, int max, ref string str, string controlName)
        {
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(40f);
            GUILayout.Label(label, GUILayout.Width(90f));
            int newVal = (int)GUILayout.HorizontalSlider((float)value, (float)min, (float)max, GUILayout.Width(180f));
            GUI.SetNextControlName(controlName);
            string input = GUILayout.TextField(str, GUILayout.Width(60f));
            bool focus = GUI.GetNameOfFocusedControl() == controlName;
            if (focus)
            {
                // 正在输入：框驱动
                str = input;
                int parsed;
                if (int.TryParse(str, out parsed)) value = parsed;
            }
            else
            {
                // 无焦点：滑块驱动
                str = value.ToString();
                value = newVal;
            }
            GUILayout.EndHorizontal();
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
        }
    }
}

