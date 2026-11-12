using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ShoAerospace.Koffee
{
    // Observe the salary's own currency query and clamp, excluding unrelated
    // transactions made recursively by currency listeners.
    internal static class SalaryPaymentCapture
    {
        private sealed class Payment
        {
            internal CurrencyModifierQuery? Query;
            internal double Clamp;
        }
        [ThreadStatic] private static Payment? pending;
        [ThreadStatic] private static Payment? active;
        internal static void Install(Harmony harmony) => harmony.Patch(
            AccessTools.Method(typeof(Funding), "AddFunds"),
            prefix: Hook(nameof(Begin)), transpiler: Hook(nameof(Observe)), finalizer: Hook(nameof(Finish)));
        private static HarmonyMethod Hook(string name) => new HarmonyMethod(AccessTools.Method(typeof(SalaryPaymentCapture), name));
        internal static double Charge(double amount, out bool captured)
        {
            var payment = new Payment();
            Payment? previous = pending; pending = payment;
            try { Funding.Instance.AddFunds(-amount, TransactionReasons.CrewRecruited); }
            finally { pending = previous; }
            // An absent query means that installation failed. Do not invent an
            // effective payment from a balance polluted by foreign callbacks.
            captured = payment.Query != null;
            if (payment.Query == null) return amount;
            return amount - payment.Query.GetEffectDelta(Currency.Funds) - payment.Clamp;
        }
        private static void Begin(out object? __state)
        { __state = active; active = pending; pending = null; }
        private static Exception? Finish(Exception? __exception, object? __state)
        { active = __state as Payment; return __exception; }
        private static CurrencyModifierQuery Query(TransactionReasons reason, float funds, float science, float reputation)
        {
            var query = new CurrencyModifierQuery(reason, funds, science, reputation);
            if (active != null) active.Query = query;
            return query;
        }
        private static double Clamp(double minimum, double funds)
        {
            double result = Math.Max(minimum, funds);
            if (active != null) active.Clamp += result - funds;
            return result;
        }
        private static IEnumerable<CodeInstruction> Observe(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList(); int queries = 0, clamps = 0;
            foreach (CodeInstruction instruction in code)
            {
                if (instruction.opcode == OpCodes.Newobj && instruction.operand is ConstructorInfo constructor &&
                    constructor.DeclaringType == typeof(CurrencyModifierQuery))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(SalaryPaymentCapture), nameof(Query)); queries++; }
                else if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Math) &&
                    method.Name == nameof(Math.Max) && method.ReturnType == typeof(double))
                { instruction.operand = AccessTools.Method(typeof(SalaryPaymentCapture), nameof(Clamp)); clamps++; }
            }
            if (queries != 1 || clamps != 1) throw new InvalidOperationException("Unknown stock salary currency path.");
            return code;
        }
    }
}
