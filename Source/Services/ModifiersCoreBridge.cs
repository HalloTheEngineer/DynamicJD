using System;
using System.Reflection;
using DynamicJD.Core;

namespace DynamicJD.Services
{
    internal static class ModifiersCoreBridge
    {
        private const string ManagerTypeName = "ModifiersCore.ModifiersManager";

        public static bool TryGetActiveSpeedModifier(out CustomSpeedModifierSpec spec)
        {
            spec = null;
            Type managerType = FindManagerType();
            MethodInfo hasModifier = FindMethod(managerType, "HasModifierWithId");
            MethodInfo getState = FindMethod(managerType, "GetModifierState");
            if (managerType == null || hasModifier == null || getState == null) return false;

            foreach (CustomSpeedModifierSpec candidate in CustomSpeedModifiers.All)
            {
                if (!IsActive(hasModifier, getState, candidate.Id)) continue;
                spec = candidate;
                return true;
            }

            return false;
        }

        private static Type FindManagerType()
        {
            Type direct = Type.GetType(ManagerTypeName + ", ModifiersCore", false);
            if (direct != null) return direct;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(ManagerTypeName, false);
                if (type != null) return type;
            }

            return null;
        }

        private static MethodInfo FindMethod(Type managerType, string name)
        {
            if (managerType == null) return null;
            return managerType.GetMethod(name, BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string) }, null);
        }

        private static bool IsActive(MethodInfo hasModifier, MethodInfo getState, string id)
        {
            try
            {
                if (!(bool)hasModifier.Invoke(null, new object[] { id })) return false;
                return (bool)getState.Invoke(null, new object[] { id });
            }
            catch (Exception ex)
            {
                Plugin.Log?.Debug("DynamicJD could not read ModifiersCore state for " + id + ": " + ex.Message);
                return false;
            }
        }
    }
}
