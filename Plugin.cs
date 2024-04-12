using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;

namespace TrueScore
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGUID = "Electric131.TrueScore";
        public const string ModName = "TrueScore";
        public const string ModVersion = "1.0.1";

        public static ManualLogSource? logger;

        public static float temp_t;
        public static float temp_time;
        public static float temp_views;

        private void Awake()
        {
            logger = Logger;
            logger.LogInfo($"Plugin {ModGUID} loaded!");

            Harmony.CreateAndPatchAll(typeof(Plugin));

            logger.LogInfo($"Patches created successfully");
        }

        [HarmonyPatch(typeof(UI_Views), "Update")]
        [HarmonyPrefix]
        public static bool UpdatePatch(ref UI_Views __instance)
        {
            if (SurfaceNetworkHandler.RoomStats == null)
            {
                return false;
            }
            int scoreToViews = BigNumbers.GetScoreToViews(SurfaceNetworkHandler.RoomStats.CurrentQuota, GameAPI.CurrentDay);
            int scoreToViews2 = BigNumbers.GetScoreToViews(SurfaceNetworkHandler.RoomStats.QuotaToReach, GameAPI.CurrentDay);
            __instance.text.text = string.Concat(new string[]
            {
                BigNumbers.ViewsToString(scoreToViews),
                "/",
                BigNumbers.ViewsToString(scoreToViews2),
                " ",
                __instance.m_ViewsText,
                " (",
                SurfaceNetworkHandler.RoomStats.CurrentQuota.ToString(),
                "/",
                SurfaceNetworkHandler.RoomStats.QuotaToReach.ToString(),
                " Score)"
            });
            return false;
        }

        [HarmonyPatch(typeof(UploadCompleteUI), nameof(UploadCompleteUI.DisplayVideoEval), MethodType.Enumerator)]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> DisplayVideoEvalPatch(IEnumerable<CodeInstruction> instructions)
        {
            return new CodeMatcher(instructions)
            .MatchForward(false,
                // Find the point right before the first insertion
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(UploadCompleteUI), nameof(UploadCompleteUI.m_viewsCurve))),
                new CodeMatch(OpCodes.Ldarg_0)
            )
            .ThrowIfInvalid("Unable to find where views text is set while video is running")
            .Advance(1) // Move to the Ldarg_0
            .InsertAndAdvance(new CodeInstruction(OpCodes.Ldnull)) // Insert Ldnull right before Ldarg_0 (and before Ldfld)
            .Advance(2) // Skip over the Ldarg_0 and Ldfld, to the next Ldarg_0 right before 't' is loaded
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Stfld, AccessTools.Field(typeof(Plugin), nameof(temp_t))), // Store the value of local variable t
                new CodeInstruction(OpCodes.Ldnull),
                new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Plugin), nameof(temp_t))),
                new CodeInstruction(OpCodes.Ldnull) // Insert Ldnull for the next Ldfld instruction
            )
            .Advance(2) // Skip over the Ldarg_0 and Ldfld, to the div instruction right before 'time' is loaded
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Stfld, AccessTools.Field(typeof(Plugin), nameof(temp_time))), // Store the value of local variable time
                new CodeInstruction(OpCodes.Ldnull),
                new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Plugin), nameof(temp_time)))
            )
            .Advance(2) // Jump to the Ldarg_0 right before the views field is loaded
            .InsertAndAdvance(new CodeInstruction(OpCodes.Ldnull)) // Insert Ldnull right before Ldarg_0 (and before Ldfld)
            .Advance(3) // Skip over the Ldarg_0 and Ldfld, to the Conv_R4 instruction right before 'views' is loaded
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Stfld, AccessTools.Field(typeof(Plugin), nameof(temp_views))), // Store the value of local variable views
                new CodeInstruction(OpCodes.Ldnull),
                new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Plugin), nameof(temp_views)))
            )
            .Advance(6) // Jump from the last point (Conv_R4) to the Ldarg_0 right after the TMP_Text.set_text is called (Callvirt)
            .Insert( // Insert custom method code right after text is set
                new CodeInstruction(OpCodes.Ldloc_1),   // this (UploadCompleteUI)
                new CodeInstruction(OpCodes.Ldc_I4_0),   // false
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Plugin), nameof(AppendScore)))
            ).MatchForward(false,
                // Find the point right before the final time the text is set (when t > time)
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(PhotonNetwork), "get_IsMasterClient"))
            )
            .ThrowIfInvalid("Unable to find where text is set when video is over")
            .Insert( // Insert custom method code right after text is set
                new CodeInstruction(OpCodes.Ldloc_1),   // this (UploadCompleteUI)
                new CodeInstruction(OpCodes.Ldc_I4_1),   // true
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Plugin), nameof(AppendScore)))
            )
            .InstructionEnumeration();
        }

        public static void AppendScore(UploadCompleteUI __instance, bool end)
        {
            if (end) { temp_t = temp_time; } // If this is the end of the video, make sure time is at the end too
            int scoreMultiplier = BigNumbers.GetScoreToViews(1, SurfaceNetworkHandler.RoomStats.CurrentDay);
            __instance.m_views.text += " (" + BigNumbers
                .ViewsToString(Mathf.FloorToInt(__instance.m_viewsCurve.Evaluate(temp_t / temp_time) * (temp_views / scoreMultiplier)))
                .ToString() + " Score)";
        }
    }
}
