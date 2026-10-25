using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // BRP 0.3.2 logs ZIP failures from Task.Run. KSP's log callback updates
    // TextMeshQueue synchronously, so even Debug.Log must stay on Unity's thread.
    internal static class StationBrpLogGuard
    {
        private readonly struct Message
        {
            internal readonly string Kind, Text;
            internal Message(string kind, string text) { Kind = kind; Text = text; }
        }

        private static readonly ConcurrentQueue<Message> pending = new ConcurrentQueue<Message>();
        private static int mainThread;

        internal static void Install(Harmony harmony)
        {
            mainThread = Thread.CurrentThread.ManagedThreadId;
            Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
                .SingleOrDefault(a => a.GetName().Name == StationBrpApi.AssemblyName);
            if (assembly == null) return;
            Type sink = assembly.GetType("BackgroundResourceProcessing.LogUtil+UnityLogSink", true)!;
            MethodInfo[] methods = new[] { "Log", "Warn", "Error" }.Select(name =>
            {
                MethodInfo? method = sink.GetMethod(name, new[] { typeof(string) });
                if (method == null || method.IsStatic || method.ReturnType != typeof(void))
                    throw new MissingMethodException(sink.FullName, name);
                return method;
            }).ToArray();
            foreach (var method in methods)
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(StationBrpLogGuard), nameof(Forward)));
        }

        private static bool Forward(string __0, MethodBase __originalMethod)
        {
            if (Thread.CurrentThread.ManagedThreadId == mainThread) return true;
            pending.Enqueue(new Message(__originalMethod.Name, __0));
            return false;
        }

        // Called by the persistent bootstrap, including across scene transitions.
        // Bound each frame without dropping diagnostics or touching them on workers.
        internal static void Flush()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread) return;
            for (int count = 0; count < 256 && pending.TryDequeue(out Message message); count++)
            {
                if (message.Kind == "Error") Debug.LogError(message.Text);
                else if (message.Kind == "Warn") Debug.LogWarning(message.Text);
                else Debug.Log(message.Text);
            }
        }
    }
}
