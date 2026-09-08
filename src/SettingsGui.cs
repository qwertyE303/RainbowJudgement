using UnityEngine;
using UnityModManagerNet;

namespace RainbowJudgement
{
    /// <summary>UnityModManager 设置页（三级开关：总开关 → 功能 → 子项）</summary>
    public static class SettingsGui
    {
        private static readonly SliderField FontSize = new SliderField("fsField", 10, 200);
        private static readonly SliderField PositionX = new SliderField("xField", -2000, 2000);
        private static readonly SliderField PositionY = new SliderField("yField", -1000, 1000);
        private static readonly SliderField Spacing = new SliderField("spField", 0, 20);

        public static void Draw(UnityModManager.ModEntry modEntry)
        {
            RainbowSettings settings = Main.Settings;

            GUILayout.BeginVertical("box", new GUILayoutOption[0]);
            GUILayout.Space(5f);

            GUIStyle title = new GUIStyle(GUI.skin.label);
            title.fontSize = 20;
            title.fontStyle = FontStyle.Bold;
            title.normal.textColor = new Color(0.55f, 0.75f, 1f);
            title.alignment = TextAnchor.MiddleCenter;
            GUILayout.Label("Rainbow Judgement", title, new GUILayoutOption[0]);
            GUILayout.Space(3f);

            // 一级：总开关
            bool enable = Toggle(settings.EnableRainbow, "Enable Rainbow Judgment", 0f);
            if (enable != settings.EnableRainbow)
            {
                settings.EnableRainbow = enable;
                if (enable)
                {
                    // 关闭期间游戏仍在计数 → 重新对齐（缺数据的条目用占位补齐，保证索引不错位）
                    Logger.Guard("SettingsGui/Enable", delegate { RainbowProgress.RealignFromGame(); });
                    Logger.Guard("SettingsGui/Enable", delegate { MeterVisualPatch.RefreshAllMeters(); });
                }
                else
                {
                    settings.ShowAverageJudgment = false;
                    Logger.Guard("SettingsGui/Disable", delegate { MeterVisualPatch.RestoreAllMeters(); });
                }
            }

            bool enabled = settings.EnableRainbow;

            // 二级：结果页平均判定
            GUI.enabled = enabled;
            bool showAverage = Toggle(settings.ShowAverageJudgment, "Show Average Judgment", 20f);
            if (showAverage != settings.ShowAverageJudgment)
            {
                settings.ShowAverageJudgment = showAverage;
                if (!showAverage) { settings.ShowAverageTime = false; settings.ShowAverageColor = false; }
            }

            // 三级：时间 / 颜色
            GUI.enabled = enabled && settings.ShowAverageJudgment;
            bool showTime = Toggle(settings.ShowAverageTime, "Show Average Absolute Deviation", 40f);
            bool showColor = Toggle(settings.ShowAverageColor, "Show Average Judgment Color", 40f);
            GUI.enabled = true;
            if (showTime != settings.ShowAverageTime) settings.ShowAverageTime = showTime;
            if (showColor != settings.ShowAverageColor) settings.ShowAverageColor = showColor;

            // 二级：彩虹计数器
            GUI.enabled = enabled;
            bool counter = Toggle(settings.ShowRainbowCounter, "Show Rainbow Counter", 20f);
            if (counter != settings.ShowRainbowCounter) settings.ShowRainbowCounter = counter;

            // 三级：计数器外观
            GUI.enabled = enabled && settings.ShowRainbowCounter;
            FontSize.Draw("Font Size", ref settings.CounterFontSize);
            PositionX.Draw("X Position", ref settings.CounterX);
            PositionY.Draw("Y Position", ref settings.CounterY);
            Spacing.Draw("Spacing", ref settings.CounterSpacing);
            GUI.enabled = true;

            // 调试日志
            settings.DebugLog = Toggle(settings.DebugLog, "Debug Log", 20f);

            GUILayout.Space(5f);
            GUILayout.EndVertical();
        }

        private static bool Toggle(bool value, string label, float indent)
        {
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            if (indent > 0f) GUILayout.Space(indent);
            bool result = GUILayout.Toggle(value, label, new GUILayoutOption[0]);
            GUILayout.EndHorizontal();
            return result;
        }

        /// <summary>滑动条 + 数值框（焦点在框内时由框驱动，否则由滑条驱动）</summary>
        private class SliderField
        {
            private readonly string _controlName;
            private readonly int _min;
            private readonly int _max;
            private string _text = "";

            public SliderField(string controlName, int min, int max)
            {
                _controlName = controlName;
                _min = min;
                _max = max;
            }

            public void Draw(string label, ref int value)
            {
                if (_text.Length == 0) _text = value.ToString();

                GUILayout.BeginHorizontal(new GUILayoutOption[0]);
                GUILayout.Space(40f);
                GUILayout.Label(label, GUILayout.Width(90f));
                int fromSlider = (int)GUILayout.HorizontalSlider(value, _min, _max, GUILayout.Width(180f));

                GUI.SetNextControlName(_controlName);
                string input = GUILayout.TextField(_text, GUILayout.Width(60f));
                if (GUI.GetNameOfFocusedControl() == _controlName)
                {
                    // 正在输入：框驱动
                    _text = input;
                    int parsed;
                    if (int.TryParse(_text, out parsed)) value = parsed;
                }
                else
                {
                    // 无焦点：滑块驱动
                    value = fromSlider;
                    _text = value.ToString();
                }
                GUILayout.EndHorizontal();
            }
        }
    }
}
