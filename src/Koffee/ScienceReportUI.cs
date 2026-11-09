using System;
using System.Linq;
using HarmonyLib;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class ScienceReportUI
    {
        internal static bool Visible;
        private static ModuleKoffeeScienceStore? focus, renaming, transferFrom;
        private static ScienceData? transferReport, labReport;
        private static string renameText = "";
        private static Vector2 scroll;
        private static Game? game;
        internal static string Text(string key, params string[] args) => Localizer.Format("#Koffee_Reports_" + key, args);
        internal static void Message(string key, params string[] args) => ScreenMessages.PostScreenMessage(Text(key, args), 5f, ScreenMessageStyle.UPPER_CENTER);
        internal static void Open(ModuleKoffeeScienceStore store, bool rename = false)
        {
            focus = store;
            if (rename) { renaming = store; renameText = store.displayName; }
            Visible = true;
            KoffeeFlightUI.ShowReports();
        }
        internal static void SelectReceiver(ModuleKoffeeScienceStore source)
        {
            if (source.Container == null || !ScienceReportTransfer.Live(source.part)) return;
            PartItemTransfer.DismissActive();
            ExperimentTransfer.Create(source.part, source.Container, (action, selected) =>
            {
                if (action != PartItemTransfer.DismissAction.ItemMoved) return;
                var receiver = selected?.FindModuleImplementing<ModuleKoffeeScienceStore>();
                if (receiver == null || receiver == source || receiver.vessel != source.vessel) Message("InvalidReceiver");
                else ScienceReportTransfer.Move(new[] { (IScienceDataContainer)source.Container }, receiver);
            });
        }
        internal static void Draw()
        {
            if (!ReferenceEquals(game, HighLogic.CurrentGame))
            {
                game = HighLogic.CurrentGame;
                if (focus == null || (HighLogic.LoadedSceneIsFlight && focus.vessel != FlightGlobals.ActiveVessel)) focus = null;
                transferFrom = null; transferReport = labReport = null;
            }
            var stores = HighLogic.LoadedSceneIsEditor
                ? EditorLogic.fetch?.ship?.parts.SelectMany(p => p.Modules.OfType<ModuleKoffeeScienceStore>()).OrderBy(m => m.installationOrder).ToArray()
                : ModuleKoffeeScienceStore.Stores(FlightGlobals.ActiveVessel).ToArray();
            if (stores == null || stores.Length == 0) { GUILayout.Label(Text("NoManagers")); return; }
            if (!stores.Contains(focus)) focus = stores[0];
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Math.Min(520, Math.Max(120, Screen.height / KoffeeStatusWindow.Scale - 190))));
            foreach (var store in stores)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(store.Context, new GUIStyle(GUI.skin.label) { wordWrap = true });
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Text("Rename"))) { renaming = store; renameText = store.displayName; }
                if (GUILayout.Button(Text("ReceiveHere"))) store.ReceiveHere();
                if (!HighLogic.LoadedSceneIsEditor && GUILayout.Button(Text("TransferAll")))
                { transferFrom = store; transferReport = null; labReport = null; }
                GUILayout.EndHorizontal();
                if (renaming == store)
                {
                    renameText = GUILayout.TextField(renameText, 80);
                    if (GUILayout.Button(Text("SaveName"))) { store.Rename(renameText); renaming = null; }
                }
                var container = store.Container;
                if (!HighLogic.LoadedSceneIsEditor && container != null)
                {
                    foreach (ScienceData report in container.GetData().ToArray())
                    {
                        GUILayout.Label(report.title, new GUIStyle(GUI.skin.label) { wordWrap = true });
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button(Text("Review"))) container.ReviewDataItem(report);
                        if (GUILayout.Button(Text("Transmit"))) AccessTools.DeclaredMethod(typeof(ModuleScienceContainer), "sendDataToComms").Invoke(container, new object[] { report });
                        if (GUILayout.Button(Text("Lab"))) { labReport = ReferenceEquals(labReport, report) ? null : report; transferReport = null; }
                        if (GUILayout.Button(Text("Transfer"))) { transferFrom = store; transferReport = report; labReport = null; }
                        GUILayout.EndHorizontal();
                        if (ReferenceEquals(labReport, report))
                        {
                            var labs = ScienceLabAcceptance.Destinations(store.vessel, report).ToArray();
                            if (labs.Length == 0) GUILayout.Label(Text("LabRejected"));
                            foreach (var lab in labs)
                                if (ScienceLabAcceptance.Preview(lab, report, out _, out _, out float amount) &&
                                    GUILayout.Button(lab.part.partInfo.title + " [" + lab.part.persistentId + "] · " + amount.ToString("0") + " " + Text("LabData")))
                                { ScienceLabAcceptance.Accept(container, report, lab); labReport = null; }
                        }
                        if (transferFrom == store && ReferenceEquals(transferReport, report)) DrawDestinations(store, report, stores);
                    }
                    if (transferFrom == store && transferReport == null) DrawDestinations(store, null, stores);
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
        }
        private static void DrawDestinations(ModuleKoffeeScienceStore source, ScienceData? data, ModuleKoffeeScienceStore[] stores)
        {
            foreach (var destination in stores.Where(m => m != source && m.Container != null && m.vessel == source.vessel &&
                (data == null || (ScienceReportPolicy.CanReceiveInManager(data) && (m.Container!.allowRepeatedSubjects || !m.Container.GetData().Any(d => d.subjectID == data.subjectID))))))
                if (GUILayout.Button("→ " + destination.Context))
                {
                    ScienceReportTransfer.Move(new[] { (IScienceDataContainer)source.Container! }, destination, data);
                    transferFrom = null; transferReport = null;
                }
            if (GUILayout.Button(Text("Cancel"))) { transferFrom = null; transferReport = null; }
        }
    }
}
