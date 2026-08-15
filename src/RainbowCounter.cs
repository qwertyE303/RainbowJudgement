using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using HarmonyLib;

namespace RainbowJudgement
{
	/// <summary>彩虹计数：PurePerfect（不含 EP/LP）按时间误差分四组（紫/青/蓝/完美绿），显示 F A B C D E G 七个数字</summary>
	public static class RainbowCounter
	{
		//F=提前·完美绿(2/3~1) A=提前·蓝(1/2~2/3,480nm) B=提前·青(1/3~1/2,440nm) C=紫(<=1/3,400nm,提前+落后) D=落后·青 E=落后·蓝 G=落后·完美绿
		public static int GreenEarly;
		public static int BlueEarly;
		public static int CyanEarly;
		public static int Purple;
		public static int CyanLate;
		public static int BlueLate;
		public static int GreenLate;
		/// <summary>本关所有 Perfect 判定的最大误差角度(度)（历史遗留，X完美无瑕已改用分组计数判断，仅 DebugLog 使用）</summary>
		public static double MaxErrorDeg = 0.0;
	public static double LastDeltaDeg = 0.0;
		/// <summary>本关是否存在 PP开外判定（LP/EP/VL/VE/Too）——决定结尾是"恭喜"还是"X^n完美无瑕"</summary>
		public static bool HasNonPerfect = false;
		/// <summary>最近一次判定是否为 Perfect（GetMarginHook 设置，AddHitHook 读取——用于游戏最终刻度分档）</summary>
		public static bool LastWasPerfect = false;
		/// <summary>最近一次判定的游戏最终刻度（AddHitHook 存：auto 强制中间时为 0；供平均颜色/平均偏差/n 使用，保证 auto 平均=0）</summary>
		public static float LastTickAngle = 0f;
		/// <summary>当前判定是否 auto触发（scrPlayer.Hit 的 isAuto/auto 属性；官方 autoplay 与自动砖块均置位）——auto 判定强制统计为完美中心（紫色）</summary>
		public static bool AutoActive = false;
		/// <summary>本关所有 Perfect判定的归一化完美度之和（p=|角度|/PP边界，0~1）</summary>
		private static double _sumPerfectP = 0.0;
		/// <summary>本关 Perfect判定次数</summary>
		private static int _perfectCount = 0;

		/// <summary>累加一次 Perfect判定的归一化完美度 p（权重1）</summary>
		public static void AddPerfectP(double p) { _sumPerfectP += p; _perfectCount++; }
		/// <summary>平均归一化完美度 r = Σp/次数</summary>
		public static double GetPerfectRatio() { return _perfectCount > 0 ? _sumPerfectP / _perfectCount : 0.0; }
		/// <summary>X^n颜色档位：存在 PP~2/3PP判定→3(540nm)；否则存在2/3PP~0.5PP→2(487nm)；否则存在0.5PP~1/3PP→1(460nm)；否则0(433nm)</summary>
		public static int GetXColorIndex()
		{
			if (GreenEarly != 0 || GreenLate != 0) return 3;
			if (BlueEarly != 0 || BlueLate != 0) return 2;
			if (CyanEarly != 0 || CyanLate != 0) return 1;
			return 0;
		}
		public static double LastScaledPos = 0.0; // 判定瞬间游戏同款缩放刻度(0~60，含 marginScale)，供判定文字颜色（与 tick 同源）
		public static double LastCountedDeg = 0.0; // 判定瞬间 Counted 边界(含 marginScale)
		public static double LastBpmTimesSpeed = 0.0; // 判定瞬间 bpm×speed
		public static double LastPitch = 1.0; // 判定瞬间 pitch
		public static double LastMarginScale = 1.0; // 判定瞬间 marginScale

		public static void Reset()
		{
			GreenEarly = BlueEarly = CyanEarly = Purple = CyanLate = BlueLate = GreenLate = 0;
			MaxErrorDeg = 0.0;
			HasNonPerfect = false;
			AutoActive = false;
			_sumPerfectP = 0.0;
			_perfectCount = 0;
		}

		/// <summary>按时间误差分组：<=1/3紫、<=1/2青、<=2/3蓝、<=1完美绿；timeMs 提前为负</summary>
		/// <summary>原版完美绿（_greenHex 解析为 Color32，供 X^n 档位色使用）</summary>
		public static Color32 GetPerfectGreenColor()
		{
			try
			{
				if (!string.IsNullOrEmpty(_greenHex) && _greenHex.Length == 6)
				{
					byte r = (byte)Convert.ToInt32(_greenHex.Substring(0, 2), 16);
					byte g = (byte)Convert.ToInt32(_greenHex.Substring(2, 2), 16);
					byte b = (byte)Convert.ToInt32(_greenHex.Substring(4, 2), 16);
					return new Color32(r, g, b, 255);
				}
			}
			catch { }
			return new Color32(95, 255, 78, 255); // fallback 原版完美绿
		}

		public static int AddByAngle(double absDeg, bool isEarly, double a1, double a2, double a3)
			{
				int g;
				if (absDeg <= a1) { Purple++; g = 0; }
				else if (absDeg <= a2) { if (isEarly) { CyanEarly++; g = 1; } else { CyanLate++; g = 2; } }
				else if (absDeg <= a3) { if (isEarly) { BlueEarly++; g = 3; } else { BlueLate++; g = 4; } }
				else { if (isEarly) { GreenEarly++; g = 5; } else { GreenLate++; g = 6; } }
				if (absDeg > MaxErrorDeg) MaxErrorDeg = absDeg;
				Refresh();
				return g;
			}
		public static void ResetCounts()
		{
			Purple = 0; CyanEarly = 0; CyanLate = 0;
			BlueEarly = 0; BlueLate = 0; GreenEarly = 0; GreenLate = 0;
			_sumPerfectP = 0.0;
			_perfectCount = 0;
		}

		/// <summary>按分组号批量累加（回档重算用）。g:0=Purple 1=CyanEarly 2=CyanLate 3=BlueEarly 4=BlueLate 5=GreenEarly 6=GreenLate</summary>
		public static void AddGroupRaw(int g, double p)
		{
			switch (g)
			{
				case 0: Purple++; break;
				case 1: CyanEarly++; break;
				case 2: CyanLate++; break;
				case 3: BlueEarly++; break;
				case 4: BlueLate++; break;
				case 5: GreenEarly++; break;
				default: GreenLate++; break;
			}
			_sumPerfectP += p; // 与 AddPerfectP 一致：p=0（auto/正中）也算入分母，保证回档重算与正常游玩 r 一致
			_perfectCount++;
		}

		private static string _greenHex = "5FFF4E"; // 原版完美绿 fallback // 原版完美绿 fallback
		private static bool _greenLoaded = false;

		/// <summary>运行时读取游戏原版完美绿色（RDConstants.hitMarginColoursUI.colourPerfect）</summary>
		private static void EnsureGreenHex()
		{
			if (_greenLoaded) return;
			_greenLoaded = true;
			try
			{
				var data = RDConstants.data;
				if (data == null) return;
				var f = data.GetType().GetField("hitMarginColoursUI", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				if (f == null) return;
				var scheme = f.GetValue(data);
				if (scheme == null) return;
				var pf = scheme.GetType().GetField("colourPerfect", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				if (pf == null) return;
				Color pc = (Color)pf.GetValue(scheme);
				_greenHex = string.Format("{0:X2}{1:X2}{2:X2}", (int)(pc.r * 255f), (int)(pc.g * 255f), (int)(pc.b * 255f));
			}
			catch { }
		}

		#region UI
		private static GameObject _canvasGO;
		private static TextMeshProUGUI _text;
		private static RectTransform _rt;
		private static int[] _last;
		private static int _lastSp = -1;

		/// <summary>清除字体描边黑边（复制材质修改，不影响游戏其他文字）</summary>
		private static void RemoveOutline()
		{
			try
			{
				if (_text == null) return;
				Material m = _text.fontMaterial;
				if (m == null) return;
				Material inst = new Material(m);
				if (inst.HasProperty("_OutlineWidth")) inst.SetFloat("_OutlineWidth", 0f);
				if (inst.HasProperty("_OutlineColor")) inst.SetColor("_OutlineColor", new Color(0f, 0f, 0f, 0f));
				if (inst.HasProperty("_UnderlayColor")) inst.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0f));
				if (inst.HasProperty("_UnderlaySoftness")) inst.SetFloat("_UnderlaySoftness", 0f);
				_text.fontMaterial = inst;
			}
			catch { }
		}

		/// <summary>右下角阴影（TMP Underlay，偏移X正右/Y负下）</summary>
		private static void AddShadow()
		{
			try
			{
				if (_text == null) return;
				Material m = _text.fontMaterial;
				if (m == null) return;
				Material inst = new Material(m);
				if (inst.HasProperty("_UnderlayColor")) inst.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.225f));
				if (inst.HasProperty("_UnderlayOffsetX")) inst.SetFloat("_UnderlayOffsetX", 0.75f);
				if (inst.HasProperty("_UnderlayOffsetY")) inst.SetFloat("_UnderlayOffsetY", -0.75f);
				if (inst.HasProperty("_UnderlaySoftness")) inst.SetFloat("_UnderlaySoftness", 0f);
				_text.fontMaterial = inst;
			}
			catch { }
		}

		/// <summary>获取TMP字体：全局默认字体 -> 已加载字体资源 -> 从游戏现有TMP文本偷取</summary>
		private static TMP_FontAsset FindTmpFont()
		{
			try
			{
				if (TMP_Settings.defaultFontAsset != null) return TMP_Settings.defaultFontAsset;
				TMP_FontAsset[] allFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
				if (allFonts != null && allFonts.Length > 0) return allFonts[0];
				TextMeshProUGUI[] all = UnityEngine.Object.FindObjectsByType<TextMeshProUGUI>(UnityEngine.FindObjectsSortMode.None);
				for (int i = 0; i < all.Length; i++)
				{
					if (all[i] != null && all[i].font != null) return all[i].font;
				}
				TextMeshProUGUI[] all2 = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>();
				for (int i = 0; i < all2.Length; i++)
				{
					if (all2[i] != null && all2[i].font != null) return all2[i].font;
				}
			}
			catch { }
			return null;
		}

		public static void EnsureUI()
		{
			if (_text != null) return;
			try
			{
				_canvasGO = new GameObject("RainbowCounterCanvas");
				UnityEngine.Object.DontDestroyOnLoad(_canvasGO);
				Canvas canvas = _canvasGO.AddComponent<Canvas>();
				canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				canvas.sortingOrder = 999;
				_canvasGO.AddComponent<CanvasScaler>();
				_canvasGO.AddComponent<RainbowCounterUpdater>();

				GameObject go = new GameObject("CounterText");
				go.transform.SetParent(_canvasGO.transform, false);
				_text = go.AddComponent<TextMeshProUGUI>();
				_text.richText = true;
				_text.raycastTarget = false;
				_text.alignment = TextAlignmentOptions.Center;
				_text.color = Color.white;
				_text.overflowMode = TextOverflowModes.Overflow;
				_text.font = FindTmpFont();
				Logger.Log("[RainbowCounter] TMP font = " + (_text.font == null ? "NULL" : _text.font.name) + " | size = " + Main.Settings.CounterFontSize + " | pos = (" + Main.Settings.CounterX + "," + Main.Settings.CounterY + ")");
				_text.fontSize = (float)Main.Settings.CounterFontSize;
				RemoveOutline();
				AddShadow();

				_rt = go.GetComponent<RectTransform>();
				_rt.sizeDelta = new Vector2(1000f, 160f);
				_rt.anchorMin = new Vector2(0.5f, 0.5f);
				_rt.anchorMax = new Vector2(0.5f, 0.5f);
				_rt.pivot = new Vector2(0.5f, 0.5f);
				_rt.anchoredPosition = new Vector2(Main.Settings.CounterX, Main.Settings.CounterY);
				Logger.Log("[RainbowCounter] UI 已创建, active=" + _text.gameObject.activeSelf);
			}
			catch (Exception ex)
			{
				Logger.Log("[RainbowCounter] UI 创建失败: " + ex.Message);
			}
		}

		public static void Hide()
		{
			if (_text != null && _text.gameObject.activeSelf) _text.gameObject.SetActive(false);
		}

		public static void Refresh()
		{
			if (!Main.Enabled || !Main.Settings.EnableRainbow || !Main.Settings.ShowRainbowCounter)
			{
				if (_text != null && _text.gameObject.activeSelf) _text.gameObject.SetActive(false);
				return;
			}
			EnsureGreenHex();
			EnsureUI();
			if (_text == null) return;
			if (_text.font == null)
			{
				TMP_FontAsset f = FindTmpFont();
				if (f != null) { _text.font = f; Logger.Log("[RainbowCounter] TMP字体已获取: " + f.name); }
			}
			if (!_text.gameObject.activeSelf) _text.gameObject.SetActive(true);

			int sp = Main.Settings.CounterSpacing;
			if (sp < 0) sp = 0;
			if (_last != null && _last[0] == GreenEarly && _last[1] == BlueEarly && _last[2] == CyanEarly && _last[3] == Purple && _last[4] == CyanLate && _last[5] == BlueLate && _last[6] == GreenLate && _lastSp == sp) return;
			_last = new int[] { GreenEarly, BlueEarly, CyanEarly, Purple, CyanLate, BlueLate, GreenLate };
			_lastSp = sp;
			string space = new string(' ', sp);
			_text.text = string.Format(
				"<color=#" + _greenHex + ">{0}</color>" + space + "<color=#006179>{1}</color>" + space + "<color=#000067>{2}</color>" + space + "<color=#390055>{3}</color>" + space + "<color=#000067>{4}</color>" + space + "<color=#006179>{5}</color>" + space + "<color=#" + _greenHex + ">{6}</color>",
				GreenEarly, BlueEarly, CyanEarly, Purple, CyanLate, BlueLate, GreenLate);
		}

		public static void ApplySettings()
		{
			if (_text == null) return;
			if (Mathf.Abs(_text.fontSize - (float)Main.Settings.CounterFontSize) > 0.01f) _text.fontSize = (float)Main.Settings.CounterFontSize;
			if (_rt != null)
			{
				Vector2 p = new Vector2(Main.Settings.CounterX, Main.Settings.CounterY);
				if (_rt.anchoredPosition != p) _rt.anchoredPosition = p;
			}
		}
		#endregion
	}

	/// <summary>挂在Canvas上的更新器：仅在关卡世界（scrConductor.isGameWorld）时显示</summary>
	public class RainbowCounterUpdater : MonoBehaviour
	{
		private void Update()
		{
			// 参考 XPerfect：仅关卡世界(isGameWorld)且未暂停(!paused)时显示；主菜单/选歌/编辑器按 esc 暂停时隐藏
			bool inGame = false;
			try
			{
				scrConductor cdt = scrConductor.instance;
				scrController ctrl = scrController.instance;
				inGame = cdt != null && ctrl != null && cdt.isGameWorld && !ctrl.paused;
			}
			catch { inGame = false; }
			if (!inGame)
			{
				RainbowCounter.Hide();
				return;
			}
			RainbowCounter.ApplySettings();
			RainbowCounter.Refresh();
		}
	}

	/// <summary>编辑器从测试切回编辑模式时立即隐藏计数（参考 XPerfect EditorSwitchToEditModePatch）</summary>
	[HarmonyPatch(typeof(scnEditor), "SwitchToEditMode")]
	public static class EditorSwitchToEditModePatch
	{
		public static void Postfix()
		{
			try { RainbowCounter.Hide(); }
			catch { }
		}
	}
}