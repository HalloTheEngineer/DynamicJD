using System;
using System.Reflection;
using DynamicJD.Configuration;
using DynamicJD.Services;
using HarmonyLib;
using UnityEngine;

namespace DynamicJD.Patches
{
    internal static class InstantNoteRotationPatch
    {
        private static readonly FieldInfo RotatedObject =
            AccessTools.Field(typeof(NoteJump), "_rotatedObject");
        private static readonly FieldInfo EndRotation =
            AccessTools.Field(typeof(NoteJump), "_endRotation");

        internal static void Patch(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(NoteJump), nameof(NoteJump.ManualUpdate));
            if (target == null || RotatedObject == null || EndRotation == null)
            {
                Plugin.Log?.Warn("DynamicJD: instant note rotation target not found; feature disabled.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(InstantNoteRotationPatch), nameof(Postfix))));
        }

        private static void Postfix(NoteJump __instance)
        {
            if (__instance == null || !ReplayGuard.ShouldApplyModBehaviors) return;
            PluginConfig config = PluginConfig.Instance;
            if (config == null || !config.instant_note_rotation) return;

            Transform rotated = RotatedObject.GetValue(__instance) as Transform;
            if (rotated == null) return;
            if (EndRotation.GetValue(__instance) is Quaternion endRotation)
            {
                rotated.localRotation = endRotation;
            }
        }
    }
}
