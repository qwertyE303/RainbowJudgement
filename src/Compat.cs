using System;
using System.Collections.Generic;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// **2.9.8 专属兼容层**：这一版游戏与 3.3.0 的差异全部收敛在这里，其他文件不直接碰"版本相关符号"。
    ///
    /// 2.9.8 与 3.3.0 的类型形状对照（本文件就是围绕这张表写的）：
    ///   · 没有 <c>scrPlayer</c>：玩家逻辑全在 <c>scrPlanet</c>（<c>currfloor</c> / <c>marginScale</c> 是每格字段）
    ///   · 没有 <c>scrMarginTracker</c>：<c>hitMargins</c> / <c>hitMarginsCount</c> 是 <c>scrMistakesManager</c> 的**静态**成员
    ///   · 没有 <c>DetailedResults</c>：结尾页文本由 <c>scrController.OnLandOnPortal</c> 拼进 <c>scrController.txtResults</c>
    ///   · 续关读档是 <c>scrMistakesManager.LoadProgress(bool)</c>（3.3.0 叫 LoadCheckpointProgress）
    ///   · <c>CalculateTickColor(float angle, float marginScale)</c> 没有 hitFloor 参数 → 需要"当前判定格"
    ///   · <c>scrHitErrorMeter.AddHit(float, float)</c> 同样没有 floor 参数
    ///   · 中旋强制 PP 的开关 <c>midspinInfiniteMargin</c> 在 **scrController** 上（3.3.0 在 scrPlayer 上）
    ///   · Unity 2018.3 没有 <c>Object.FindObjectsByType</c>（2019.4 才有）
    /// </summary>
    public static class Compat
    {
        // ---------------- 当前判定格 ----------------

        /// <summary>
        /// "这次判定对应的那一格"（= 玩家即将落上的那一格）。
        /// 2.9.8 里就是 <c>controller.chosenPlanet.currfloor.nextfloor</c>：
        /// SwitchChosen 取的 <c>currfloor.nextfloor</c> 正是判定目标格，而 3.3.0 直接把这个 floor
        /// 作为参数传给了 GetHitMargin / CalculateTickColor / AddHit。
        /// 该字段**只在 Main.Load 时设置一次**：SetAccessors 在 csc 编译期确定了字段/属性，
        /// 此后反射_getter 只是普通委托调用（其中没有任何反射），可以安全地在逐帧的 tick 路径上读。
        /// </summary>
        public static volatile scrFloor CurrentFloor;

        private static Func<scrFloor> _floorGetter;

        /// <summary>解析"当前判定格"的读取方式（成功返回 true，失败则 CurrentFloor 恒为 null）</summary>
        public static bool SetAccessors()
        {
            try
            {
                System.Reflection.PropertyInfo prop = typeof(scrController).GetProperty("chosenPlanet");
                System.Reflection.FieldInfo field = typeof(scrController).GetField("chosenPlanet");

                if (prop != null)
                {
                    System.Reflection.MethodInfo getter = prop.GetGetMethod(true);
                    if (getter != null)
                    {
                        _floorGetter = delegate
                        {
                            scrController ctrl = scrController.instance;
                            if (ctrl == null) return null;
                            scrPlanet planet = getter.Invoke(ctrl, null) as scrPlanet;
                            return planet != null ? planet.currfloor : null;
                        };
                        return true;
                    }
                }
                if (field != null)
                {
                    _floorGetter = delegate
                    {
                        scrController ctrl = scrController.instance;
                        if (ctrl == null) return null;
                        scrPlanet planet = field.GetValue(ctrl) as scrPlanet;
                        return planet != null ? planet.currfloor : null;
                    };
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[Compat] 当前判定格访问器解析失败: " + ex.Message);
            }
            return false;
        }

        /// <summary>刷新"当前判定格"（GetHitMargin / tick 取色 / 判别自动砖共同使用）</summary>
        public static void CaptureCurrentFloor()
        {
            if (_floorGetter == null) return;
            try { CurrentFloor = _floorGetter(); }
            catch { }
        }

        /// <summary>玩家当前所在格（2.9.8 无 scrPlayer，等价物是 chosenPlanet.currfloor）</summary>
        public static scrFloor PlayerCurrFloor()
        {
            if (_floorGetter == null) return null;
            try { return _floorGetter(); }
            catch { return null; }
        }

        // ---------------- 中旋（midspin）强制 PP 的读取 ----------------
        //
        // 说明：这里**故意不再提供**任何"本帧发生过 auto 判定"的标记接口。
        // 原因是 2.9.8 里唯一相关的例行方法 scrController.HitAutoFloors 由
        // Simulated_PlayerControl_Update **每帧**调用，用它当命中信号会让所有人工判定都被当成 auto、
        // 误差被清零（实测表现为"全部 0ms 紫色"）。
        // auto / 强制 PP 的判定权现在完全交给游戏自己的记账真值：
        // 见 JudgeHooks.GetMarginHook 的中旋当场识别 + MistakesAddHitHook 的 MergeGameGrade 校正。

        /// <summary>读当前关卡的 <c>scrController.midspinInfiniteMargin</c>（中旋无限判定窗）。
        /// 该字段在 2.9.8 里位于 scrController 上（3.3.0 移到了 scrPlayer）。
        /// 返回值表示"是否读到了"；读不到时 value 恒为 false。</summary>
        public static bool ReadMidspinInfiniteMargin(out bool value)
        {
            value = false;
            try
            {
                scrController ctrl = scrController.instance;
                if (ctrl == null) return false;
                value = ctrl.midspinInfiniteMargin;
                return true;
            }
            catch { return false; }
        }

        // ---------------- 对象查找（Unity 2018.3 ↔ 2019.4 API 差异） ----------------

        /// <summary>找出场景中全部 T 类型组件。Unity 2018.3 没有 FindObjectsByType 泛型重载，
        /// 因此优先反射调用它（3.3.0 那棵树若共用本文件也能工作），失败再退到 FindObjectsOfType。</summary>
        public static T[] FindAll<T>() where T : UnityEngine.Object
        {
            try
            {
                System.Reflection.MethodInfo method = typeof(UnityEngine.Object).GetMethod("FindObjectsByType",
                    new Type[] { typeof(FindObjectsSortMode) });
                if (method != null)
                {
                    System.Reflection.MethodInfo generic = method.MakeGenericMethod(typeof(T));
                    object result = generic.Invoke(null, new object[] { FindObjectsSortMode.None });
                    T[] array = result as T[];
                    if (array != null) return array;
                }
            }
            catch { }

            try { return UnityEngine.Object.FindObjectsOfType<T>(); }
            catch { }
            return new T[0];
        }
    }
}
