using System;
using System.Linq;
using System.Reflection;

namespace ShoAerospace.Koffee
{
    // Inspect only assemblies already loaded by KSP. Do not load an optional
    // DLL from disk or introduce a CLR reference from the main Koffee assembly.
    // Compatible API != running solver: the adapter must also initialize before
    // StationPowerProviderSelection can choose BRP.
    internal static class StationBrpApi
    {
        internal const string AssemblyName = "BackgroundResourceProcessing";
        private const string Namespace = "BackgroundResourceProcessing.";

        internal static bool Compatible(Assembly[] assemblies, out string detail)
        {
            if (assemblies == null) throw new ArgumentNullException(nameof(assemblies));
            Assembly[] matches = assemblies.Where(a => a.GetName().Name == AssemblyName).ToArray();
            if (matches.Length != 1)
            {
                detail = matches.Length == 0 ? "BRP is not loaded." : "Multiple BRP assemblies are loaded.";
                return false;
            }
            Assembly assembly = matches[0];
            try
            {
                Type processor = RequireType(assembly, Namespace + "BackgroundResourceProcessor");
                Type change = RequireType(assembly, Namespace + "ChangepointEvent");
                Type converter = RequireType(assembly, Namespace + "Core.ResourceConverter");
                Type behaviour = RequireType(assembly, Namespace + "Behaviour.ConverterBehaviour");
                Type state = RequireType(assembly, Namespace + "VesselState");
                RequireMethod(processor, "UpdateBackgroundState", typeof(void));
                RequireMethod(processor, "MarkDirty", typeof(void));
                RequireMethod(behaviour, "GetResources",
                    RequireType(assembly, Namespace + "ConverterResources"), state);
                RequireField(change, "LastChangepoint", typeof(double));
                RequireField(change, "CurrentChangepoint", typeof(double));
                RequireField(converter, "Rate", typeof(double));
                RequireField(converter, "ActiveTime", typeof(double));
                RequireProperty(converter, "Behaviour", behaviour);
                foreach (string name in new[] { "onStateUpdate", "onVesselRecord", "onVesselRestore" })
                {
                    FieldInfo field = processor.GetField(name, BindingFlags.Public | BindingFlags.Static) ??
                        throw new MissingFieldException(processor.FullName, name);
                    Type[] arguments = field.FieldType.GetGenericArguments();
                    Type[] expected = name == "onStateUpdate" ? new[] { processor, change } : new[] { processor };
                    if (!field.IsInitOnly || !arguments.SequenceEqual(expected) ||
                        field.FieldType.Name != (name == "onStateUpdate" ? "EventData`2" : "EventData`1"))
                        throw new InvalidOperationException("Unsupported BRP event: " + name);
                }
                detail = assembly.GetName().FullName;
                return true;
            }
            catch (Exception error)
            {
                detail = "BRP API is unavailable: " + error.GetType().Name + ": " + error.Message;
                return false;
            }
        }

        private static Type RequireType(Assembly assembly, string name) => assembly.GetType(name, true)!;
        private static void RequireField(Type type, string name, Type value)
        {
            FieldInfo? field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field?.FieldType != value) throw new MissingFieldException(type.FullName, name);
        }
        private static void RequireProperty(Type type, string name, Type value)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property?.PropertyType != value || property.GetGetMethod() == null)
                throw new MissingMemberException(type.FullName, name);
        }
        private static void RequireMethod(Type type, string name, Type result, params Type[] arguments)
        {
            MethodInfo? method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance,
                null, arguments, null);
            if (method?.ReturnType != result) throw new MissingMethodException(type.FullName, name);
        }
    }
}
