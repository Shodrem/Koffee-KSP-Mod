using System.Reflection;
using HarmonyLib;

namespace ShoAerospace.Koffee
{
    internal static class ConverterTiming
    {
        private static readonly FieldInfo LastUpdateTime = AccessTools.Field(
            typeof(BaseConverter), "lastUpdateTime");

        internal static double UpdateEndUT(BaseConverter converter) =>
            (double)LastUpdateTime.GetValue(converter)!;
    }
}
