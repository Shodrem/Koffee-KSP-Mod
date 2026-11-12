using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSP.Localization;
using KSP.UI.Screens;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CrewWidget = KSP.UI.Screens.SpaceCenter.MissionSummaryDialog.CrewWidget;

namespace ShoAerospace.Koffee
{
    internal static class SalaryRecoveryUI
    {
        private static readonly System.Reflection.FieldInfo CrewContainer = AccessTools.Field(typeof(MissionRecoveryDialog), "wContainerCrew");
        private static readonly System.Reflection.FieldInfo ResourceContainer = AccessTools.Field(typeof(MissionRecoveryDialog), "wContainerResources");
        private static readonly System.Reflection.FieldInfo Header = AccessTools.Field(typeof(MissionRecoveryDialog), "tabHeaderField");
        private static readonly System.Reflection.FieldInfo PartsAvailable = AccessTools.Field(typeof(MissionRecoveryDialog), "PartsModeAvailable");
        private static readonly ConditionalWeakTable<MissionRecoveryDialog, SalaryRecoveryReceipt> Receipts =
            new ConditionalWeakTable<MissionRecoveryDialog, SalaryRecoveryReceipt>();
        internal static void Install(Harmony harmony)
        {
            SalaryPaymentCapture.Install(harmony);
            harmony.Patch(AccessTools.Method(typeof(MissionRecoveryDialog), "updateCrewWindowContent"),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(SalaryRecoveryUI), nameof(Crew))));
            harmony.Patch(AccessTools.Method(typeof(MissionRecoveryDialog), "updatePartsWindowContent"),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(SalaryRecoveryUI), nameof(Funds))));
        }
        internal static void Attach(MissionRecoveryDialog dialog, SalaryRecoveryReceipt receipt)
        {
            Receipts.Remove(dialog); Receipts.Add(dialog, receipt);
            dialog.fundsEarned = receipt.NetFunds;
            dialog.totalFunds = receipt.TotalFunds;
            // An EVA may have no recoverable part widgets. Its existing Parts
            // view still needs to expose the salary and net funds summary.
            PartsAvailable.SetValue(dialog, true);
        }
        private static void Crew(MissionRecoveryDialog __instance)
        {
            if (!Receipts.TryGetValue(__instance, out var receipt)) return;
            try
            {
                var widgets = AccessTools.Field(typeof(MissionRecoveryDialog), "crewWidgets").GetValue(__instance) as IEnumerable<CrewWidget>;
                if (widgets == null) return;
                var container = (RectTransform)CrewContainer.GetValue(__instance);
                foreach (var widget in widgets)
                {
                    if (widget == null || widget.crew == null) continue;
                    SalaryRecoveryRow? row = receipt.Crew.Find(c => c.Name == widget.crew.name);
                    if (row == null) continue;
                    // A sibling row follows the stock crew card in its existing
                    // scroll layout; stock status, XP and level fields stay intact.
                    Transform text = container.Find("KoffeeSalary:" + row.Id);
                    if (text == null)
                        text = AddRow(container, widget.statusField, "KoffeeSalary:" + row.Id, row.Text);
                    text.SetSiblingIndex(widget.RTrf.GetSiblingIndex() + 1);
                }
                if (container.Find("KoffeeSalaryTotal") == null)
                    AddRow(container, (TextMeshProUGUI)Header.GetValue(__instance), "KoffeeSalaryTotal", receipt.TotalText);
                container.Find("KoffeeSalaryTotal").SetAsLastSibling();
                LayoutRebuilder.MarkLayoutForRebuild(container);
            }
            catch (Exception error) { Debug.LogError("[Koffee] Salary crew summary failed: " + error); }
        }
        private static Transform AddRow(RectTransform parent, TextMeshProUGUI template, string name, string value)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
            var rect = (RectTransform)row.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(.5f, 1);
            var text = row.GetComponent<TextMeshProUGUI>();
            text.font = template.font; text.fontSharedMaterial = template.fontSharedMaterial;
            text.fontSize = template.fontSize; text.color = template.color;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.enableWordWrapping = true; text.raycastTarget = false;
            text.margin = new Vector4(12, 4, 12, 6); text.text = value;
            float width = Math.Max(180, parent.rect.width);
            float height = text.GetPreferredValues(value, width - 24, Mathf.Infinity).y + 14;
            rect.sizeDelta = new Vector2(0, height);
            var layout = row.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = height; layout.flexibleWidth = 1;
            return rect;
        }
        private static void Funds(MissionRecoveryDialog __instance)
        {
            if (!Receipts.TryGetValue(__instance, out var receipt)) return;
            try
            {
                // The stock footer already shows the net amount and final balance.
                var container = (RectTransform)ResourceContainer.GetValue(__instance);
                if (container.Find("KoffeeSalaryFunds") == null)
                    AddRow(container, (TextMeshProUGUI)Header.GetValue(__instance), "KoffeeSalaryFunds",
                        Localizer.Format("#Koffee_Salary_RecoveryFunds",
                            SalaryRecoveryRow.Number(receipt.RecoveredFunds), SalaryRecoveryRow.Number(-receipt.Charged),
                            SalaryRecoveryRow.Number(receipt.NetFunds)));
                LayoutRebuilder.MarkLayoutForRebuild(container);
            }
            catch (Exception error) { Debug.LogError("[Koffee] Salary funds summary failed: " + error); }
        }
    }
}
