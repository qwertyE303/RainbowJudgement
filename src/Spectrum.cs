using System;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 可见光谱波长 &lt;-&gt; RGB 转换工具。
    /// 亮度修正使 380nm=深紫(77,0,77)、700nm=纯红(255,0,0)，且 645nm 处分段亮度因子连续（避免 R 值跳变）。
    /// </summary>
    public static class Spectrum
    {
        public const double MinWavelengthNm = 380.0;   // 可见光谱最短波长（深紫）
        public const double MaxWavelengthNm = 780.0;   // 可见光谱最长波长（红）

        /// <summary>波长(nm) -&gt; RGB。Dan Bruton 分段线性近似 + 亮度修正</summary>
        public static Color32 WavelengthToRgb(double lambdaNm)
        {
            lambdaNm = Math.Max(MinWavelengthNm, Math.Min(MaxWavelengthNm, lambdaNm));
            double r = 0, g = 0, b = 0;
            if (lambdaNm >= 380 && lambdaNm < 440) { r = (440 - lambdaNm) / 60.0; g = 0; b = 1; }
            else if (lambdaNm < 490) { r = 0; g = (lambdaNm - 440) / 50.0; b = 1; }
            else if (lambdaNm < 510) { r = 0; g = 1; b = (510 - lambdaNm) / 20.0; }
            else if (lambdaNm < 580) { r = (lambdaNm - 510) / 70.0; g = 1; b = 0; }
            else if (lambdaNm < 645) { r = 1; g = (645 - lambdaNm) / 65.0; b = 0; }
            else { r = 1; g = 0; b = 0; }

            double factor; // 亮度因子：在 645nm 处连续（0.76375），700nm 起为 1.0（纯红 255,0,0）
            if (lambdaNm <= 645.0) factor = 0.3 + 0.7 * (lambdaNm - 380) / 400.0;
            else if (lambdaNm <= 700.0) factor = 0.76375 + 0.23625 * (lambdaNm - 645.0) / 55.0;
            else factor = 1.0;

            return new Color32(
                (byte)Math.Round(255.0 * r * factor),
                (byte)Math.Round(255.0 * g * factor),
                (byte)Math.Round(255.0 * b * factor),
                255);
        }

        public static string ToHex(Color32 c)
        {
            return string.Format("{0:X2}{1:X2}{2:X2}", c.r, c.g, c.b);
        }

        /// <summary>解析 "RRGGBB"（可带 #），失败返回 fallback</summary>
        public static Color32 ParseHex(string hex, Color32 fallback)
        {
            try
            {
                if (!string.IsNullOrEmpty(hex))
                {
                    if (hex[0] == '#') hex = hex.Substring(1);
                    if (hex.Length == 6)
                    {
                        byte r = (byte)Convert.ToInt32(hex.Substring(0, 2), 16);
                        byte g = (byte)Convert.ToInt32(hex.Substring(2, 2), 16);
                        byte b = (byte)Convert.ToInt32(hex.Substring(4, 2), 16);
                        return new Color32(r, g, b, 255);
                    }
                }
            }
            catch { }
            return fallback;
        }
    }
}
