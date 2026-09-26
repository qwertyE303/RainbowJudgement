using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 「编辑自定义判定」那块表格编辑器（原来是 SettingsGui 里的一大段，拆出来单独维护）。
    /// 结构：表头（角度/时间/最小时间/颜色）→ 每档的标题行（▼ / 启用 / #编号 / 颜色 / Del）
    ///       → 展开后严格/标准/宽松三行 × 三个输入框 → 颜色下一级（波长 / RGB）。
    /// 所有列的宽度都取自下面那组常量，四种行共用同一套 → 列边界天然重合。
    /// 输入缓冲与展开状态由 SettingsGui 持有（RowEditor / RowBuffer / _expandedTiers）。
    /// </summary>
    internal static class CustomTierEditor
    {
        internal static bool DrawCustomEditor(RainbowSettings settings)
        {
            bool dirty = false;

            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(40f);
            string caption = (SettingsGui._editorExpanded ? "\u25BC " : "\u25B6 ") + Lang.T("editCustom");
            bool expanded = GUILayout.Toggle(SettingsGui._editorExpanded, caption, GUI.skin.button, GUILayout.Width(260f));
            GUILayout.EndHorizontal();
            if (expanded != SettingsGui._editorExpanded) SettingsGui._editorExpanded = expanded;
            if (!SettingsGui._editorExpanded) return false;

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

            // 表头：角度（°） | 时间（ms） | 最小时间（ms） | 颜色
            // 四列宽度是固定值（见上面的列宽常量）；表头、难度行、标题行、波长/RGB 行共用同一套，
            // 所以列边界由同一组数字保证重合 —— 不做任何"读回面板宽度"的自适应。
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(PrefixWidth);
            GUILayout.Label(Lang.T("colDeg"), GUILayout.Width(ColumnNumeric));
            GUILayout.Label(Lang.T("colJudgeTime"), GUILayout.Width(ColumnNumeric));
            GUILayout.Label(Lang.T("colTime"), GUILayout.Width(ColumnNumeric));
            GUILayout.Label(Lang.T("colColor"), GUILayout.Width(ColumnSwatch));
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
                SettingsGui.RowEditor row = SettingsGui.RowBuffer(i, tier);

                // ---- 标题行：下拉箭头 + 启用 + #编号 + 颜色 + Del（**不显示角度**）----
                GUILayout.BeginHorizontal(new GUILayoutOption[0]);
                GUILayout.Space(TableIndent);

                bool wasExpanded = SettingsGui._expandedTiers.Contains(i);
                bool nowExpanded = GUILayout.Toggle(wasExpanded, wasExpanded ? "\u25BC" : "\u25B6",
                    GUI.skin.button, GUILayout.Width(ColumnArrow));
                if (nowExpanded != wasExpanded)
                {
                    if (nowExpanded) SettingsGui._expandedTiers.Add(i); else SettingsGui._expandedTiers.Remove(i);
                }

                bool wasEnabled = tier.Enabled;
                tier.Enabled = GUILayout.Toggle(tier.Enabled, "", GUILayout.Width(ColumnEnable));
                if (tier.Enabled != wasEnabled) dirty = true;

                GUILayout.Label("#" + (i + 1), GUILayout.Width(ColumnIndex));

                // 补位：让 [▼][启用][#编号] + 本补位 的总宽正好等于 ColorStart，
                // 于是颜色勾选框与色标精确落进 COL_COLOR 列（与表头「颜色」同列、与难度行第 4 格同位）。
                // 全部用表达式（不写死数字），所以改上面任何一个列宽常量都不会让色标跑偏。
                GUILayout.Space(ColorStart - (TableIndent + ColumnArrow + ColumnEnable + ColumnIndex));

                bool wasCustom = tier.UseCustomColor;
                tier.UseCustomColor = GUILayout.Toggle(tier.UseCustomColor, "", GUILayout.Width(ToggleWidth));
                if (tier.UseCustomColor != wasCustom) dirty = true;
                SettingsGui.Swatch(CustomJudge.DigitColor(tier));
                // 补足 COL_COLOR 列剩余宽度；这里用固定 Space（不用 FlexibleSpace——
                // 弹性空间会把色标顶出这一列）。
                float swatchFill = ColumnSwatch - ToggleWidth - SwatchWidth;
                if (swatchFill > 0f) GUILayout.Space(swatchFill);

                // 补到整表宽度：Del 落在所有行共用的右边界上（Del 仍在一行末尾，只是整行右对齐）
                float used = ColorStart + ColumnSwatch;
                float slack = TableWidth - used;
                if (slack > 0f) GUILayout.Space(slack);
                if (GUILayout.Button("Del", GUILayout.Width(ColumnDel))) removeAt = i;
                GUILayout.EndHorizontal();

                // ---- 展开：严格 / 标准 / 宽松 三行，每行 3 个输入框 ----
                if (nowExpanded)
                {
                    dirty |= DrawDifficultyRow(row, tier, i, TierDifficulty.Strict);
                    dirty |= DrawDifficultyRow(row, tier, i, TierDifficulty.Normal);
                    dirty |= DrawDifficultyRow(row, tier, i, TierDifficulty.Lenient);
                }

                if (tier.UseCustomColor) dirty |= DrawColorSubLevel(i, tier, row);
            }

            if (removeAt >= 0)
            {
                CustomJudge.RemoveAt(removeAt);
                dirty = true;
            }
            return dirty;
        }

        /// <summary>一个难度行：行标签 + 判定角度 / 判定时间 / 最小限制时间（列宽与表头共用，保证对齐）</summary>
        private static bool DrawDifficultyRow(SettingsGui.RowEditor row, CustomTierDef tier, int tierIndex, int difficulty)
        {
            TierRow data = tier.Row(difficulty);
            bool blank = data.IsBlank;
            bool dirty = false;

            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(TableIndent + ColumnArrow);
            GUILayout.Label(DifficultyLabel(difficulty), GUILayout.Width(ColumnLabel));

            string prefix = "t" + tierIndex + "d" + difficulty + "_";
            dirty |= RowCell(prefix + "deg", row, difficulty, 0, ref data.Deg, ColumnNumeric, blank);
            dirty |= RowCell(prefix + "jt", row, difficulty, 1, ref data.JudgeTimeMs, ColumnNumeric, blank);
            dirty |= RowCell(prefix + "mt", row, difficulty, 2, ref data.MinTimeMs, ColumnNumeric, blank);
            GUILayout.Space(ColumnSwatch);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            return dirty;
        }

        private static string DifficultyLabel(int difficulty)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            switch (difficulty)
            {
                case TierDifficulty.Lenient:
                    style.normal.textColor = new Color(0.65f, 0.85f, 1f);
                    break;
                case TierDifficulty.Normal:
                    style.normal.textColor = new Color(1f, 1f, 1f);
                    break;
                default:
                    style.normal.textColor = new Color(1f, 0.72f, 0.55f);
                    break;
            }
            return Lang.T(TierDifficulty.LabelKey(difficulty));
        }

        /// <summary>行内数值输入；<paramref name="blankRow"/> = 该行还没填过（空输入框）</summary>
        private static bool RowCell(string controlName, SettingsGui.RowEditor row, int difficulty, int field,
            ref double value, float width, bool blankRow)
        {
            GUI.SetNextControlName(controlName);
            string buffer = row.Get(difficulty, field);
            string shown = buffer.Length > 0 ? buffer : (blankRow ? "" : SettingsGui.ShowDouble(value));
            string input = GUILayout.TextField(shown, GUILayout.Width(width));

            if (GUI.GetNameOfFocusedControl() == controlName)
            {
                row.Set(difficulty, field, input);
                double parsed = SettingsGui.ParseDouble(input);
                if (parsed != value) { value = parsed; return true; }
                return false;
            }
            row.Set(difficulty, field, "");
            return false;
        }

        /// <summary>自定义颜色的下一级：波长 / RGB 二选一（互斥），RGB 必须自带 '#'</summary>
        private static bool DrawColorSubLevel(int index, CustomTierDef tier, SettingsGui.RowEditor row)
        {
            bool dirty = false;
            // 缩进 = COL_COLOR 列的绝对起点，让波长/RGB 与表头「颜色」列（以及难度行第 4 格）左对齐
            float indent = ColorStart;

            // 波长
            GUILayout.BeginHorizontal(new GUILayoutOption[0]);
            GUILayout.Space(indent);
            GUILayout.Label(Lang.T("wavelength") + "：", GUILayout.Width(110f));
            bool waveSelected = !tier.RgbMode;
            bool waveNow = GUILayout.Toggle(waveSelected, "", GUILayout.Width(20f));
            if (waveNow != waveSelected && waveNow) { tier.RgbMode = false; dirty = true; }
            bool previous = GUI.enabled;
            GUI.enabled = previous && !tier.RgbMode;
            dirty |= SettingsGui.DoubleCell("wl" + index, ref row.Wl, ref tier.WavelengthNm, 80f);
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
            dirty |= SettingsGui.HexCell("rgb" + index, ref row.Rgb, ref tier.RgbHex, 100f);
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

        // ---------------- 列宽（固定值；表头与所有数据行共用，保证对齐） ----------------
        //
        // 【要改列宽就改这里的数字】——全部是固定像素，不做任何自适应（自适应会让标题行的
        // 对齐补位跟着变大，右侧空白越调越大，所以放弃）。
        //
        // 列布局（从左到右）：
        //   [TableIndent] [COL_LABEL] [COL_NUM] [COL_NUM] [COL_NUM] [COL_COLOR]
        //   · 表头     ：角度（°） / 时间（ms） / 最小时间（ms） / 颜色
        //   · 难度行   ：严格·标准·宽松  / 三个输入格 / 空位
        //   · 标题行   ：[▼][启用][#编号] + 补位 → 颜色勾选 + 色标，Del 放行末
        //   · 波长/RGB ：缩进到 ColorStart → 与 COL_COLOR 列左对齐

        /// <summary>表格整体左缩进（与 New/Sort/Reset 那一排对齐）</summary>
        private const float TableIndent = 60f;
        private const float ColumnArrow = 24f;      // 下拉箭头（难度行用同宽空位对齐）
        private const float ColumnIndex = 34f;
        private const float ColumnEnable = 26f;
        private const float ColumnDel = 46f;
        private const float SwatchWidth = 22f;      // SettingsGui.Swatch() 实际画的宽度
        private const float ToggleWidth = 24f;      // 颜色勾选框的宽度

        /// <summary>行首列：放"严格/标准/宽松"（表头那格放"角度（°）"）</summary>
        private const float ColumnLabel = 40f;
        /// <summary>三个数值列（角度 / 时间 / 最小时间）</summary>
        private const float ColumnNumeric = 160f;
        /// <summary>"颜色"列（颜色勾选 + 色标；也与表头「颜色」同列）</summary>
        private const float ColumnSwatch = 160f;

        /// <summary>COL_COLOR 列的绝对起点（标题行的补位、波长/RGB 行的缩进都用它）</summary>
        private const float ColorStart = TableIndent + ColumnArrow + ColumnLabel + 3f * ColumnNumeric -10f;

        /// <summary>"数值列之前"的固定宽度（标题行、难度行共用）</summary>
        private const float PrefixWidth = TableIndent + ColumnArrow + ColumnLabel + 40f;

        /// <summary>整张表的右端。标题行补到这个宽度，Del 就落在所有行的同一条右边界上。</summary>
        private const float TableWidth = ColorStart + ColumnSwatch + ColumnDel;
    }
}
