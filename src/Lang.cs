using System;
using System.Collections.Generic;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 全 Mod 的文案表（唯一出口，别在别处硬编码字符串）：
    ///   · 设置界面 → 由 <see cref="RainbowSettings.GuiLanguage"/> 决定（中文 / English / 日本語 / 한국어）
    ///   · 结果页标签 → 始终跟随**游戏语言**（与 v1.1.0 行为一致，覆盖 11 种语言）
    /// 新增文案只需在 <see cref="Build"/> 里加一行。
    /// </summary>
    public static class Lang
    {
        public const int Chinese = 1;
        public const int English = 2;
        public const int Japanese = 3;
        public const int Korean = 4;

        /// <summary>语言按钮的显示名（与当前语言无关，各语言用自己的写法）</summary>
        public static readonly string[] Names = { "中文", "English", "日本語", "한국어" };

        /// <summary>设置界面文案：key → [中文, English, 日本語, 한국어]</summary>
        private static readonly Dictionary<string, string[]> Table = Build();

        // ---------------- 设置界面 ----------------

        public static string T(string key)
        {
            string[] row;
            if (Table.TryGetValue(key, out row))
            {
                string text = row[Index(Resolve())];
                if (!string.IsNullOrEmpty(text)) return text;
                return row[Index(English)];
            }
            return key;
        }

        /// <summary>当前 GUI 语言（1~4）；未设置时按游戏语言预选</summary>
        public static int Resolve()
        {
            try
            {
                int value = Main.Settings != null ? Main.Settings.GuiLanguage : 0;
                if (value >= Chinese && value <= Korean) return value;
            }
            catch { }
            return DetectFromGame();
        }

        /// <summary>按游戏语言预选（只认这四种，其余回退英文）。玩家在设置页点过语言按钮后就不再走这里。</summary>
        public static int DetectFromGame()
        {
            try
            {
                switch (Persistence.language)
                {
                    case SystemLanguage.Chinese:
                    case SystemLanguage.ChineseSimplified:
                    case SystemLanguage.ChineseTraditional:
                        return Chinese;
                    case SystemLanguage.Japanese: return Japanese;
                    case SystemLanguage.Korean: return Korean;
                }
            }
            catch { }
            return English;
        }

        private static int Index(int language)
        {
            int i = language - 1;
            return i < 0 ? 0 : (i > 3 ? 1 : i);
        }

        // ---------------- 结果页标签（跟随游戏语言） ----------------

        /// <summary>结果页标签：average = 平均判定，customCount = 自定义判定</summary>
        public static string ResultsLabel(string key)
        {
            bool custom = key == "customCount";
            try
            {
                switch (Persistence.language)
                {
                    case SystemLanguage.Chinese:
                    case SystemLanguage.ChineseSimplified:
                    case SystemLanguage.ChineseTraditional:
                    case SystemLanguage.Japanese:
                        return custom ? "自定义判定" : "平均判定";
                    case SystemLanguage.Korean:
                        return custom ? "사용자 지정 판정" : "평균 판정";
                    case SystemLanguage.Spanish:
                        return custom ? "Juicio personalizado" : "Juicio promedio";
                    case SystemLanguage.French:
                        return custom ? "Jugement personnalisé" : "Jugement moyen";
                    case SystemLanguage.German:
                        return custom ? "Benutzerdefiniertes Urteil" : "Durchschnittliches Urteil";
                    case SystemLanguage.Russian:
                        return custom ? "Пользовательское суждение" : "Среднее суждение";
                    case SystemLanguage.Portuguese:
                        return custom ? "Julgamento personalizado" : "Julgamento médio";
                    case SystemLanguage.Polish:
                        return custom ? "Osąd niestandardowy" : "Średni osąd";
                    case SystemLanguage.Italian:
                        return custom ? "Giudizio personalizzato" : "Giudizio medio";
                    case SystemLanguage.Turkish:
                        return custom ? "Özel yargı" : "Ortalama yargı";
                    default:
                        return custom ? "Custom Judgement" : "Average Judgement";
                }
            }
            catch { return custom ? "Custom Judgement" : "Average Judgement"; }
        }

        // ---------------- 表 ----------------

        private static Dictionary<string, string[]> Build()
        {
            Dictionary<string, string[]> t = new Dictionary<string, string[]>(StringComparer.Ordinal);

            // 一级
            Add(t, "enableRainbow", "开启彩虹判定", "Enable Rainbow Judgement", "レインボー判定を有効化", "무지개 판정 사용");

            // 二级：判定详情
            Add(t, "showDetails", "显示判定详情", "Show Judgement Details", "判定詳細を表示", "판정 상세 표시");
            Add(t, "endPage", "结尾页详情", "Results Page Details", "リザルト画面の詳細", "결과 화면 상세");
            Add(t, "live", "实时详情", "In-Game Live Details", "リアルタイム詳細", "실시간 상세");
            Add(t, "avgColor", "平均判定颜色", "Average Judgement Colour", "平均判定の色", "평균 판정 색");
            Add(t, "avgTime", "平均绝对时间偏差", "Average Absolute Time Deviation", "平均絶対時間偏差", "평균 절대 시간 편차");
            Add(t, "avgAngle", "平均绝对角度偏差", "Average Absolute Angle Deviation", "平均絶対角度偏差", "평균 절대 각도 편차");

            // 实时文本项自己的名字
            Add(t, "liveColor", "平均颜色", "Average Colour", "平均色", "평균 색");

            // 实验性
            Add(t, "experimental", "----实验性选项------", "----Experimental Options------", "----実験的オプション------", "----실험적 옵션------");
            Add(t, "enableCustom", "开启自定义判定", "Enable Custom Judgements", "カスタム判定を有効化", "사용자 지정 판정 사용");
            Add(t, "editCustom", "编辑自定义判定", "Edit Custom Judgements", "カスタム判定を編集", "사용자 지정 판정 편집");
            Add(t, "showCount", "显示判定计数", "Show Judgement Counts", "判定カウントを表示", "판정 카운트 표시");
            Add(t, "countInResults", "结尾页显示计数详情", "Show Count Details on Results", "リザルトにカウント詳細を表示", "결과 화면에 카운트 상세 표시");
            Add(t, "colDeg", "判定角度", "Angle", "判定角度", "판정 각도");
            Add(t, "colTime", "最小限制时间", "Min Time", "最小時間", "최소 시간");
            Add(t, "colColor", "自定义颜色", "Custom Colour", "カスタム色", "사용자 지정 색");
            Add(t, "wavelength", "波长（nm）", "Wavelength (nm)", "波長（nm）", "파장(nm)");
            Add(t, "rgb", "RGB", "RGB", "RGB", "RGB");
            Add(t, "invalid", "无效", "Invalid", "無効", "잘못된 값");
            Add(t, "noTiers", "（没有判定条目：点 New 新增）", "(No entries: press New to add)", "（項目なし：New で追加）", "(항목 없음: New로 추가)");
            Add(t, "tierDisabled", "（禁用）", "(disabled)", "（無効）", "(사용 안 함)");

            // 通用小标签
            Add(t, "fontSize", "字号", "Font Size", "フォントサイズ", "글자 크기");
            Add(t, "xPos", "X 位置", "X Position", "X 位置", "X 위치");
            Add(t, "yPos", "Y 位置", "Y Position", "Y 位置", "Y 위치");
            Add(t, "spacing", "间距", "Spacing", "間隔", "간격");
            Add(t, "align", "对齐", "Align", "配置", "정렬");
            Add(t, "alignLeft", "左对齐", "Left", "左寄せ", "왼쪽");
            Add(t, "alignCenter", "居中", "Center", "中央", "가운데");
            Add(t, "alignRight", "右对齐", "Right", "右寄せ", "오른쪽");
            Add(t, "text", "文本", "Text", "テキスト", "텍스트");
            Add(t, "debugLog", "调试日志", "Debug Log", "デバッグログ", "디버그 로그");

            return t;
        }

        private static void Add(Dictionary<string, string[]> table, string key, string zh, string en, string ja, string ko)
        {
            table[key] = new string[] { zh, en, ja, ko };
        }
    }
}
