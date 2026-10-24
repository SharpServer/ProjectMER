using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;
using ProjectMER.Features;
using UnityEngine;

namespace ProjectMER.Patches;

/// <summary>Native structures never restore their schematic parent on clients.</summary>
[HarmonyPatch(typeof(NetworkServer), nameof(NetworkServer.SendSpawnMessage))]
internal static class StructureSpawnTransformPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var position = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.localPosition));
        var rotation = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.localRotation));
        var scale = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.localScale));
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(position))
            { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(StructureSpawnTransformPatch), nameof(Position)); }
            else if (instruction.Calls(rotation))
            { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(StructureSpawnTransformPatch), nameof(Rotation)); }
            else if (instruction.Calls(scale))
            { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(StructureSpawnTransformPatch), nameof(Scale)); }
            yield return instruction;
        }
    }

    private static bool IsMerStructure(Transform transform) => transform.GetComponent<StructureTransformSync>() != null;
    private static Vector3 Position(Transform transform) => IsMerStructure(transform) ? transform.position : transform.localPosition;
    private static Quaternion Rotation(Transform transform) => IsMerStructure(transform) ? transform.rotation : transform.localRotation;
    private static Vector3 Scale(Transform transform) => IsMerStructure(transform) ? transform.lossyScale : transform.localScale;
}
