using System;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 彩虹仪表盘纹理：优先读用户自定义 PNG（assets\ 或 Mod 根目录），否则按实测几何用代码生成。
    /// 颜色：中心=完美=深紫 380nm → 渐变 → 红 700nm；边缘为恒定红。
    /// 生成后缓存（纹理只做一次）。
    /// </summary>
    public static class MeterTexture
    {
        // ---- 纹理尺寸（与原版/XPerfect 一致）----
        private const int Width = 400;
        private const int Height = 200;

        // ---- 横条几何（视觉坐标，y=0 在顶部；实测值）----
        private const int ShadowY0 = 66, ShadowY1 = 131, ShadowX0 = 34, ShadowX1 = 365; // 粗线黑色阴影
        private const int ColorY0 = 94, ColorY1 = 105, ColorX0 = 41, ColorX1 = 358;      // 细线彩色
        private const int RedLeft = 48, RedRight = 351;                                   // 两侧恒定红
        private const float StraightCenterX = 199.5f;                                     // 彩色渐变对称轴
        private const float StraightHalfSpan = 151.5f;                                    // 对称轴到恒定红的距离

        // ---- 弧形几何（圆心在底部附近，实测值）----
        private const float CurvedCx = 199f, CurvedCy = 198f;
        private const float ShadowRIn = 116f, ShadowROut = 181f, ShadowHalfAngle = 67f;   // 大弧阴影
        private const float ColorRIn = 145f, ColorROut = 157f, ColorHalfAngle = 64f;      // 小弧彩色
        private const float RedStartAngle = 61f;                                          // 61°~64° 恒定红

        // ---- 边缘过渡 ----
        private const float ShadowAlphaFade = 1.5f;  // 黑色阴影 → 透明
        private const float ColorAlphaFade = 1.5f;   // 横条彩色左右竖边
        private const float CurvedColorFade = 0.75f; // 弧形彩色边缘
        private const int ShadowAlpha = 84;          // 33%

        private static Sprite _straight;
        private static Sprite _curved;

        public static Sprite Straight()
        {
            if (_straight == null)
            {
                Texture2D texture = LoadPng("straight_meter.png");
                if (texture == null) texture = BuildStraight();
                _straight = MakeSprite(texture);
            }
            return _straight;
        }

        public static Sprite Curved()
        {
            if (_curved == null)
            {
                Texture2D texture = LoadPng("curved_meter.png");
                if (texture == null) texture = BuildCurved();
                _curved = MakeSprite(texture);
            }
            return _curved;
        }

        // ---------------- 用户自定义图片 ----------------

        /// <summary>加载用户 PNG（400x200，RGBA）。查找方式见 ModPaths：基准目录=实际加载的 DLL 目录，目录内 assets\ 优先。</summary>
        private static Texture2D LoadPng(string fileName)
        {
            Texture2D texture = null;
            try
            {
                string path = ModPaths.FindFile(ModPaths.Aliases(fileName));
                if (path == null)
                {
                    if (Main.Settings.DebugLog)
                        Logger.Log("[MeterVisualPatch] 未找到自定义图片 " + fileName + "（基准目录：" + ModPaths.BaseDir + "）→ 代码生成");
                    return null;
                }
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                if (texture.LoadImage(bytes))
                {
                    Logger.Log("[MeterVisualPatch] 已加载自定义仪表盘图片: " + path);
                    return texture;
                }
                UnityEngine.Object.Destroy(texture);
                return null;
            }
            catch (Exception ex)
            {
                if (texture != null) { try { UnityEngine.Object.Destroy(texture); } catch { } }
                Logger.Log("[MeterVisualPatch] 加载自定义图片失败，回退代码生成: " + ex.Message);
                return null;
            }
        }

        // ---------------- 代码生成 ----------------

        private static Texture2D BuildStraight()
        {
            Texture2D texture = NewTexture();
            Color32[] pixels = new Color32[Width * Height];

            for (int y = 0; y < Height; y++)          // y = 视觉坐标（0 在顶部）
            {
                for (int x = 0; x < Width; x++)
                {
                    Color32 color = new Color32(0, 0, 0, 0);

                    if (InRect(x, y, ShadowX0, ShadowX1, ShadowY0, ShadowY1))
                    {
                        float edge = Mathf.Min(Mathf.Min(y - ShadowY0, ShadowY1 - y), Mathf.Min(x - ShadowX0, ShadowX1 - x));
                        color = new Color32(0, 0, 0, EdgeAlpha(edge, ShadowAlpha));
                    }

                    if (InRect(x, y, ColorX0, ColorX1, ColorY0, ColorY1))
                    {
                        float edge = Mathf.Min(x - ColorX0, ColorX1 - x);
                        Color32 bar = (x < RedLeft || x > RedRight)
                            ? RainbowMath.RedColor
                            : ColorFromT(Mathf.Abs(x - StraightCenterX) / StraightHalfSpan);
                        color = EdgeToShadow(bar, edge, ColorAlphaFade);
                    }

                    pixels[(Height - 1 - y) * Width + x] = color; // Unity 纹理 y=0 在底部，翻转行序
                }
            }
            return Apply(texture, pixels);
        }

        private static Texture2D BuildCurved()
        {
            Texture2D texture = NewTexture();
            Color32[] pixels = new Color32[Width * Height];

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    float dx = x - CurvedCx;
                    float dy = y - CurvedCy;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    float theta = Mathf.Atan2(dx, -dy) * Mathf.Rad2Deg;
                    float absTheta = Mathf.Abs(theta);

                    Color32 color = new Color32(0, 0, 0, 0);

                    if (radius >= ShadowRIn && radius <= ShadowROut && absTheta <= ShadowHalfAngle)
                    {
                        float edgeRadius = Mathf.Min(radius - ShadowRIn, ShadowROut - radius);
                        float edgeAngle = (ShadowHalfAngle - absTheta) * Mathf.Deg2Rad * radius;
                        color = new Color32(0, 0, 0, EdgeAlpha(Mathf.Min(edgeRadius, edgeAngle), ShadowAlpha));
                    }

                    if (radius >= ColorRIn && radius <= ColorROut && absTheta <= ColorHalfAngle)
                    {
                        Color32 arc = absTheta >= RedStartAngle ? RainbowMath.RedColor : ColorFromT(absTheta / RedStartAngle);
                        float edgeRadius = Mathf.Min(radius - ColorRIn, ColorROut - radius);
                        float edgeAngle = (ColorHalfAngle - absTheta) * Mathf.Deg2Rad * radius;
                        color = EdgeToShadow(arc, Mathf.Min(edgeRadius, edgeAngle), CurvedColorFade);
                    }

                    pixels[(Height - 1 - y) * Width + x] = color;
                }
            }
            return Apply(texture, pixels);
        }

        private static Texture2D NewTexture()
        {
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        private static Texture2D Apply(Texture2D texture, Color32[] pixels)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Sprite MakeSprite(Texture2D texture)
        {
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }

        // ---------------- 颜色工具 ----------------

        /// <summary>归一化位置 t（0=中心完美 → 1=红）→ 颜色（不透明）</summary>
        private static Color32 ColorFromT(float t)
        {
            double wavelength = Spectrum.MinWavelengthNm + (RainbowMath.RedWavelengthNm - Spectrum.MinWavelengthNm) * t;
            return Spectrum.WavelengthToRgb(wavelength);
        }

        private static bool InRect(int x, int y, int x0, int x1, int y0, int y1)
        {
            return x >= x0 && x <= x1 && y >= y0 && y <= y1;
        }

        /// <summary>边缘透明度渐变：距边界 &lt;1.5px 时按比例降低 alpha</summary>
        private static byte EdgeAlpha(float edge, int baseAlpha)
        {
            if (edge >= ShadowAlphaFade) return (byte)baseAlpha;
            if (edge <= 0f) return 0;
            return (byte)(baseAlpha * (edge / ShadowAlphaFade));
        }

        /// <summary>彩色边缘过渡：RGB 从黑(0,0,0)过渡到彩色，alpha 从 33% 过渡到 100%</summary>
        private static Color32 EdgeToShadow(Color32 color, float edge, float width)
        {
            if (edge >= width) return color;
            if (edge <= 0f) return new Color32(0, 0, 0, ShadowAlpha);
            return Color32.Lerp(new Color32(0, 0, 0, ShadowAlpha), color, edge / width);
        }
    }
}
