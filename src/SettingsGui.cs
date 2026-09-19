using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityModManagerNet;

namespace RainbowJudgement
{
    /// <summary>
    /// UnityModManager 设置页。结构：
    ///   语言四按钮（最上面）→ 标题 → 总开关
    ///     → 显示判定详情｛结尾页详情（颜色/时间/角度）、实时详情（三项各自 开关+字号+XY）｝
    ///     → ----实验性选项------（黄字）
    ///        → 开启自定义判定 → 编辑自定义判定（下拉）→ 显示判定计数 → 结尾页显示计数详情
    ///     → Debug Log
    /// 任何档位定义变化都会立刻重算整套统计（见 RainbowProgress.OnLayoutChanged）。
    /// 控件一律无分配（不用委托/闭包），避免拖慢 UMM 的 IMGUI 重绘。
    /// </summary>
    public static class SettingsGui
    {
        private static readonly Dictionary<string, IntField> IntFields = new Dictionary<string, IntField>();
        private static readonly Dictionary<string, StringField> StringFields = new Dictionary<string, StringField>();
        private static readonly List<RowEditor> RowBuffers = new List<RowEditor>();

        private static bool _editorExpanded; // 折叠状态只是 UI 状态，不落盘
        private static Texture2D _white;

        public static void Draw(UnityModManager.ModEntry modEntry)
        {
            RainbowSettings settings = Main.Settings;
            if (settings == null) return;
            settings.EnsureLiveTexts(); // 首次打开设置页时把实时文本的默认值落下来（此时游戏语言已就绪）

            GUILayout.BeginVertical("box", new GUILayoutOption[0]);
            GUILayout.Space(5f);

            DrawTitle();
            DrawLanguage(settings);

            bool layoutDirty = false;   // 档位定义 / 实验开关变化 → 整套重算
            bool valueDirty = false;    // 只影响显示 → 刷新即可
            bool now;

            // ---------------- 一级：总开关 ----------------
            if (ToggleLang(settings.EnableRainbow, "enableRainbow", 0f, out now))
            {
                settings.EnableRainbow = now;
                if (now)
                {
                    // 关闭期间游戏仍在计数 → 重新对齐（缺数据的条目用占位补齐，保证索引不错位）
                    Logger.Guard("SettingsGui/Enable", delegate { RainbowProgress.RealignFromGame(); });
                    Logger.Guard("SettingsGui/Enable", delegate { MeterVisualPatch.RefreshAllMeters(); });
                }
                else
                {
                    settings.ShowJudgeDetails = false;
                    Logger.Guard("SettingsGui/Disable", delegate { MeterVisualPatch.RestoreAllMeters(); });
                }
                valueDirty = true;
            }

            bool enabled = settings.EnableRainbow;

            // ---------------- 二级：显示判定详情 ----------------
            GUI.enabled = enabled;
            if (ToggleLang(settings.ShowJudgeDetails, "showDetails", 20f, out now))
            {
                settings.ShowJudgeDetails = now;
                if (!now)
                {
                    settings.ShowAverageTime = false;
                    settings.ShowAverageColor = false;
                    settings.ShowAverageAngle = false;
                }
                valueDirty = true;
            }

            // 三级：结尾页详情
            GUI.enabled = enabled && settings.ShowJudgeDetails;
            Section("endPage", 40f);
            if (ToggleLang(settings.ShowAverageTime, "avgTime", 60f, out now)) { settings.ShowAverageTime = now; valueDirty = true; }
            if (ToggleLang(settings.ShowAverageColor, "avgColor", 60f, out now)) { settings.ShowAverageColor = now; valueDirty = true; }
            if (ToggleLang(settings.ShowAverageAngle, "avgAngle", 60f, out now)) { settings.ShowAverageAngle = now; valueDirty = true; }

            // 三级：实时详情（三项各自开关 + 字号 + XY）
            Section("live", 40f);
            valueDirty |= LiveItem(settings, "liveColor", ref settings.ShowLiveColor,
                "lcSize", ref settings.LiveColorFontSize, "lcx", ref settings.LiveColorX, "lcy", ref settings.LiveColorY,
                ref settings.LiveColorAlign, "lcText", ref settings.LiveColorText);
            valueDirty |= LiveItem(settings, "avgTime", ref settings.ShowLiveTime,
                "ltSize", ref settings.LiveTimeFontSize, "ltx", ref settings.LiveTimeX, "lty", ref settings.LiveTimeY,
                ref settings.LiveTimeAlign, "ltText", ref settings.LiveTimeText);
            valueDirty |= LiveItem(settings, "avgAngle", ref settings.ShowLiveAngle,
                "laSize", ref settings.LiveAngleFontSize, "lax", ref settings.LiveAngleX, "lay", ref settings.LiveAngleY,
                ref settings.LiveAngleAlign, "laText", ref settings.LiveAngleText);

            // ---------------- 实验性选项 ----------------
            GUI.enabled = true;
            GUILayout.Space(6f);
            ExperimentalHeader();

            GUI.enabled = enabled;
            if (ToggleLang(settings.EnableCustomJudge, "enableCustom", 20f, out now))
            {
                settings.EnableCustomJudge = now;
                layoutDirty = true;   // 锚点集与计数都随之变化
            }
            GUI.enabled = true;

            // 编辑自定义判定（无开关，始终可编辑；下拉展开）
            layoutDirty |= DrawCustomEditor(settings);

            // 显示判定计数
            GUI.enabled = enabled && settings.EnableCustomJudge;
            if (ToggleLang(settings.ShowCustomCount, "showCount", 40f, out now)) { settings.ShowCustomCount = now; valueDirty = true; }

            GUI.enabled = enabled && settings.EnableCustomJudge && settings.ShowCustomCount;
            valueDirty |= IntRow("countSize", "fontSize", 10, 200, ref settings.CustomCountFontSize);
            valueDirty |= IntRow("countX", "xPos", -2000, 2000, ref settings.CustomCountX);
            valueDirty |= IntRow("countY", "yPos", -1000, 1000, ref settings.CustomCountY);
            valueDirty |= IntRow("countSp", "spacing", 0, 20, ref settings.CustomCountSpacing);

            GUI.enabled = enabled && settings.EnableCustomJudge;
            if (ToggleLang(settings.ShowCustomCountInResults, "countInResults", 40f, out now)) { settings.ShowCustomCountInResults = now; valueDirty = true; }
            GUI.enabled = true;

            // ---------------- 调试 ----------------
            if (ToggleLang(settings.DebugLog, "debugLog", 20f, out now)) settings.DebugLog = now;

            GUILayout.Space(5f);
            GUILayout.EndVertical();

            if (layoutDirty) RainbowProgress.OnLayoutChanged();
            else if (valueDirty) RainbowProgress.RefreshDisplay();
        }

        // ---------------- 标题 / 语言 ----------------

        private static void DrawTitle()
        {
            GUIStyle title = new GUIStyle(GUI.skin.label);
            title.fontSize = 20;
            title.fontStyle = FontStyle.Bold;
            title.normal.textColor = new Color(0.55f, 0.75f, 1f);
            title.alignment = TextAnchor.MiddleCenter;
            GUILayout.Label("Rainbow Judgement", title, new GUILayoutOption[0]);
            GUILayout.Space(3f);
        }

        /// <summary>语言四按钮：标题下方第一行（界面自己的语言，与游戏语言无关）</summary>
        private static void DrawLanguage(RainbowSettings settings)
        {
            int current = Lang.Resolve();
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Label("Language", GUILayout.Width(80f));
            for (int i = 0; i < Lang.Names.Length; i++)
            {
                bool selected = current == i + 1;
                bool picked = GUILayout.Toggle(selected, Lang.Names[i], GUI.skin.button, GUILayout.Width(80f));
                if (picked != selected) settings.GuiLanguage = i + 1;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(2f);
        }

        // ---------------- 通用控件 ----------------

        /// <summary>开关；返回是否变化，新值通过 out 传出（不分配委托）</summary>
        private static bool ToggleLang(bool value, string key, float indent, out bool now)
        {
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            if (indent > 0f) GUILayout.Space(indent);
            now = GUILayout.Toggle(value, Lang.T(key), new GUILayoutOption[0]);
            GUILayout.EndHorizontal();
            return now != value;
        }

        private static void Section(string key, float indent)
        {
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(indent);
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontStyle = FontStyle.Bold;
            GUILayout.Label(Lang.T(key), style, new GUILayoutOption[0]);
            GUILayout.EndHorizontal();
        }

        private static void ExperimentalHeader()
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.yellow;
            GUILayout.Label(Lang.T("experimental"), style, new GUILayoutOption[0]);
        }

        private static bool IntRow(string key, string labelKey, int min, int max, ref int value)
        {
            IntField field;
            if (!IntFields.TryGetValue(key, out field))
            {
                field = new IntField(key, min, max);
                IntFields[key] = field;
            }
            return field.Draw(Lang.T(labelKey), ref value);
        }

        // ---------------- 实时详情：一项 = 开关 + 字号 + X + Y + 对齐 + 文本 ----------------

        private static bool LiveItem(RainbowSettings settings, string labelKey, ref bool show,
            string sizeKey, ref int fontSize, string xKey, ref int x, string yKey, ref int y,
            ref int align, string textKey, ref string text)
        {
            bool dirty = false;
            bool now;
            if (ToggleLang(show, labelKey, 60f, out now)) { show = now; dirty = true; }

            bool previous = GUI.enabled;
            GUI.enabled = previous && show;
            dirty |= IntRow(sizeKey, "fontSize", 10, 200, ref fontSize);
            dirty |= IntRow(xKey, "xPos", -2000, 2000, ref x);
            dirty |= IntRow(yKey, "yPos", -1000, 1000, ref y);
            dirty |= AlignRow(ref align, 60f);
            dirty |= TextRow(textKey, ref text, 60f);
            GUI.enabled = previous;
            return dirty;
        }

        /// <summary>对齐三选一（0 左 / 1 居中 / 2 右）：点已选中的那个不改变状态</summary>
        private static bool AlignRow(ref int align, float indent)
        {
            bool dirty = false;
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(indent);
            GUILayout.Label(Lang.T("align"), GUILayout.Width(90f));
            for (int i = 0; i < 3; i++)
            {
                bool selected = align == i;
                string option = i == 0 ? "alignLeft" : (i == 2 ? "alignRight" : "alignCenter");
                bool picked = GUILayout.Toggle(selected, Lang.T(option), GUI.skin.button, GUILayout.Width(70f));
                if (picked && !selected) { align = i; dirty = true; }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return dirty;
        }

        private static bool TextRow(string key, ref string value, float indent)
        {
            StringField field;
            if (!StringFields.TryGetValue(key, out field))
            {
                field = new StringField(key);
                StringFields[key] = field;
            }
            return field.Draw(Lang.T("text"), ref value, indent);
        }

        // ---------------- 编辑自定义判定 ----------------

        private static bool DrawCustomEditor(RainbowSettings settings)
        {
            bool dirty = false;

            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(40f);
            string caption = (_editorExpanded ? "\u25BC " : "\u25B6 ") + Lang.T("editCustom");
            bool expanded = GUILayout.Toggle(_editorExpanded, caption, GUI.skin.button, GUILayout.Width(260f));
            GUILayout.EndHorizontal();
            if (expanded != _editorExpanded) _editorExpanded = expanded;
            if (!_editorExpanded) return false;

            List<CustomTierDef> tiers = settings.CustomTiers;
            if (tiers == null)
            {
                tiers = new List<CustomTierDef>();
                settings.CustomTiers = tiers;
            }

            // New / Sort / Reset
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(60f);
            if (GUILayout.Button("New", GUILayout.Width(70f)))
            {
                CustomJudge.AddTier();
                tiers = settings.CustomTiers;
                dirty = true;
            }
            if (GUILayout.Button("Sort", GUILayout.Width(70f)))
            {
                CustomJudge.SortTiers();
                dirty = true;
            }
            if (GUILayout.Button("Reset", GUILayout.Width(70f)))
            {
                CustomJudge.ResetTiers();
                tiers = settings.CustomTiers;
                dirty = true;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 表头（与下面每行的三列严格对齐；#序号 / 启用 / Del 没有标题）
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(60f + ColumnIndex + ColumnEnable);
            GUILayout.Label(Lang.T("colDeg"), GUILayout.Width(ColumnDeg));
            GUILayout.Label(Lang.T("colTime"), GUILayout.Width(ColumnTime));
            GUILayout.Label(Lang.T("colColor"), GUILayout.Width(ColumnColor));
            GUILayout.EndHorizontal();

            if (tiers.Count == 0)
            {
                GUILayout.BeginHorizontal(new GUILayoutOption[0]);
                GUILayout.Space(60f);
                GUILayout.Label(Lang.T("noTiers"), new GUILayoutOption[0]);
                GUILayout.EndHorizontal();
                return dirty;
            }

            int removeAt = -1;
            for (int i = 0; i < tiers.Count; i++)
            {
                CustomTierDef tier = tiers[i];
                if (tier == null)
                {
                    tier = new CustomTierDef();
                    tiers[i] = tier;
                }
                RowEditor row = RowBuffer(i, tier);

                GUILayout.BeginHorizontal(new GUILayoutOption[0]);
                GUILayout.Space(60f);
                GUILayout.Label("#" + (i + 1), GUILayout.Width(ColumnIndex));

                bool wasEnabled = tier.Enabled;
                tier.Enabled = GUILayout.Toggle(tier.Enabled, "", GUILayout.Width(ColumnEnable));
                if (tier.Enabled != wasEnabled) dirty = true;

                dirty |= DoubleCell("deg" + i, ref row.Deg, ref tier.Deg, ColumnDeg);
                dirty |= DoubleCell("time" + i, ref row.Time, ref tier.MinTimeMs, ColumnTime);

                bool wasCustom = tier.UseCustomColor;
                tier.UseCustomColor = GUILayout.Toggle(tier.UseCustomColor, "", GUILayout.Width(24f));
                if (tier.UseCustomColor != wasCustom) dirty = true;
                Swatch(CustomJudge.DigitColor(tier));
                GUILayout.Space(ColumnColor - 24f - 24f);

                if (GUILayout.Button("Del", GUILayout.Width(ColumnDel))) removeAt = i;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (tier.UseCustomColor) dirty |= DrawColorSubLevel(i, tier, row);
            }

            if (removeAt >= 0)
            {
                CustomJudge.RemoveAt(removeAt);
                dirty = true;
            }
            return dirty;
        }

        /// <summary>自定义颜色的下一级：波长 / RGB 二选一（互斥），RGB 必须自带 '#'</summary>
        private static bool DrawColorSubLevel(int index, CustomTierDef tier, RowEditor row)
        {
            bool dirty = false;
            float indent = 60f + ColumnIndex + ColumnEnable + 12f;

            // 波长
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(indent);
            GUILayout.Label(Lang.T("wavelength") + "：", GUILayout.Width(110f));
            bool waveSelected = !tier.RgbMode;
            bool waveNow = GUILayout.Toggle(waveSelected, "", GUILayout.Width(20f));
            if (waveNow != waveSelected && waveNow) { tier.RgbMode = false; dirty = true; }
            bool previous = GUI.enabled;
            GUI.enabled = previous && !tier.RgbMode;
            dirty |= DoubleCell("wl" + index, ref row.Wl, ref tier.WavelengthNm, 80f);
            GUI.enabled = previous;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // RGB
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(indent);
            GUILayout.Label(Lang.T("rgb") + "：", GUILayout.Width(110f));
            bool rgbSelected = tier.RgbMode;
            bool rgbNow = GUILayout.Toggle(rgbSelected, "", GUILayout.Width(20f));
            if (rgbNow != rgbSelected && rgbNow) { tier.RgbMode = true; dirty = true; }
            previous = GUI.enabled;
            GUI.enabled = previous && tier.RgbMode;
            dirty |= HexCell("rgb" + index, ref row.Rgb, ref tier.RgbHex, 100f);
            GUI.enabled = previous;

            if (tier.RgbMode && !CustomJudge.IsValidHex(tier.RgbHex))
            {
                GUIStyle warn = new GUIStyle(GUI.skin.label);
                warn.normal.textColor = new Color(1f, 0.35f, 0.35f);
                GUILayout.Label(Lang.T("invalid"), warn, GUILayout.Width(60f));
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return dirty;
        }

        // ---------------- 列宽（表头与数据行共用，保证对齐） ----------------

        private const float ColumnIndex = 34f;
        private const float ColumnEnable = 26f;
        private const float ColumnDeg = 76f;
        private const float ColumnTime = 100f;
        private const float ColumnColor = 112f;
        private const float ColumnDel = 46f;

        // ---------------- 行内输入控件 ----------------

        private sealed class RowEditor
        {
            public CustomTierDef Owner;
            public string Deg = "";
            public string Time = "";
            public string Wl = "";
            public string Rgb = "";
        }

        private static RowEditor RowBuffer(int index, CustomTierDef owner)
        {
            while (RowBuffers.Count <= index) RowBuffers.Add(new RowEditor());
            RowEditor row = RowBuffers[index];
            if (!ReferenceEquals(row.Owner, owner))
            {
                row.Owner = owner;
                row.Deg = "";
                row.Time = "";
                row.Wl = "";
                row.Rgb = "";
            }
            return row;
        }

        /// <summary>行内数值输入：焦点内解析（**空串按 0 算**），无焦点时回显当前数值。
        /// 值为 NaN 时显示空框（只有波长栏会用到：该档不对应任何波长）。</summary>
        private static bool DoubleCell(string controlName, ref string buffer, ref double value, float width)
        {
            if (buffer.Length == 0) buffer = ShowDouble(value);
            GUI.SetNextControlName(controlName);
            string input = GUILayout.TextField(buffer, GUILayout.Width(width));
            if (GUI.GetNameOfFocusedControl() == controlName)
            {
                buffer = input;
                double parsed = ParseDouble(input);
                if (parsed != value) { value = parsed; return true; }
                return false;
            }
            buffer = ShowDouble(value);
            return false;
        }

        private static string ShowDouble(double value)
        {
            if (double.IsNaN(value)) return "";
            return FormatDouble(value);
        }

        /// <summary>行内 RGB 输入（原样保留玩家输入，是否合法由 CustomJudge.IsValidHex 判断）</summary>
        private static bool HexCell(string controlName, ref string buffer, ref string value, float width)
        {
            if (buffer.Length == 0) buffer = value ?? "";
            GUI.SetNextControlName(controlName);
            string input = GUILayout.TextField(buffer, GUILayout.Width(width));
            if (GUI.GetNameOfFocusedControl() == controlName)
            {
                buffer = input;
                if (input != value) { value = input; return true; }
                return false;
            }
            buffer = value ?? "";
            return false;
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static double ParseDouble(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0.0;
            double parsed;
            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return parsed;
            return 0.0;
        }

        /// <summary>色块（自定义颜色预览）</summary>
        private static void Swatch(Color color)
        {
            Rect rect = GUILayoutUtility.GetRect(22f, 18f);
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, WhiteTexture);
            GUI.color = previous;
        }

        private static Texture2D WhiteTexture
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1);
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply();
                }
                return _white;
            }
        }

        // ---------------- 滑条 + 数值框 ----------------

        /// <summary>文本输入（实时显示的自定义前缀；内容原样使用，不跟随语言）</summary>
        private class StringField
        {
            private readonly string _controlName;
            private string _text;

            public StringField(string controlName)
            {
                _controlName = controlName;
            }

            public bool Draw(string label, ref string value, float indent)
            {
                bool changed = false;
                if (_text == null) _text = value ?? "";

                GUILayout.BeginHorizontal(new GUILayoutOption[0]);
                GUILayout.Space(indent);
                GUILayout.Label(label, GUILayout.Width(90f));
                GUI.SetNextControlName(_controlName);
                string input = GUILayout.TextField(_text, GUILayout.Width(260f));
                if (GUI.GetNameOfFocusedControl() == _controlName)
                {
                    _text = input;
                    if (input != value) { value = input; changed = true; }
                }
                else
                {
                    _text = value ?? "";
                }
                GUILayout.EndHorizontal();
                return changed;
            }
        }

        /// <summary>滑动条 + 数值框（焦点在框内时由框驱动，否则由滑条驱动）</summary>
        private class IntField
        {
            private readonly string _controlName;
            private readonly int _min;
            private readonly int _max;
            private string _text = "";

            public IntField(string controlName, int min, int max)
            {
                _controlName = controlName;
                _min = min;
                _max = max;
            }

            public bool Draw(string label, ref int value)
            {
                bool changed = false;
                if (_text.Length == 0) _text = value.ToString(CultureInfo.InvariantCulture);

                GUILayout.BeginHorizontal(new GUILayoutOption[0]);
                GUILayout.Space(60f);
                GUILayout.Label(label, GUILayout.Width(90f));
                int fromSlider = (int)GUILayout.HorizontalSlider(value, _min, _max, GUILayout.Width(180f));

                GUI.SetNextControlName(_controlName);
                string input = GUILayout.TextField(_text, GUILayout.Width(60f));
                if (GUI.GetNameOfFocusedControl() == _controlName)
                {
                    // 正在输入：框驱动
                    _text = input;
                    int parsed;
                    if (int.TryParse(_text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed != value)
                    {
                        value = parsed;
                        changed = true;
                    }
                }
                else
                {
                    // 无焦点：滑块驱动
                    if (fromSlider != value)
                    {
                        value = fromSlider;
                        changed = true;
                    }
                    _text = value.ToString(CultureInfo.InvariantCulture);
                }
                GUILayout.EndHorizontal();
                return changed;
            }
        }
    }
}
