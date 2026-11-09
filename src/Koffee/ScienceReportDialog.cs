using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KSP.UI.Screens.Flight.Dialogs;
using KSP.UI.TooltipTypes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShoAerospace.Koffee
{
    // An attached row leaves the stock controls, callbacks and layout intact.
    internal sealed class ScienceReportDialog : MonoBehaviour
    {
        private static readonly FieldInfo KeepButton = AccessTools.Field(typeof(ExperimentsResultDialog), "btnKeep");
        private static readonly FieldInfo Pages = AccessTools.Field(typeof(ExperimentsResultDialog), "pages");
        private static readonly MethodInfo RemovePage = AccessTools.DeclaredMethod(typeof(ExperimentsResultDialog), "removePage");
        private ExperimentsResultDialog dialog = null!;
        private GameObject? row;
        private Button button = null!;
        private TextMeshProUGUI label = null!;
        private TooltipController_Text tooltip = null!;
        private Image led = null!;
        private float nextRefresh;
        private bool failed;

        internal static void Install(Harmony harmony)
        {
            foreach (string name in new[] { "DisplayResult", "SetPage" })
                harmony.Patch(AccessTools.DeclaredMethod(typeof(ExperimentsResultDialog), name),
                    postfix: new HarmonyMethod(typeof(ScienceReportDialog), name == "DisplayResult" ? nameof(Displayed) : nameof(PageChanged)));
        }
        private static void Displayed(ExperimentsResultDialog __result)
        {
            if (__result == null) return;
            var view = __result.GetComponent<ScienceReportDialog>() ?? __result.gameObject.AddComponent<ScienceReportDialog>();
            view.dialog = __result;
            view.Refresh();
        }
        private static void PageChanged(ExperimentsResultDialog __instance)
        {
            var view = __instance.GetComponent<ScienceReportDialog>();
            if (view != null) view.Refresh();
        }
        private void LateUpdate()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .2f;
            Refresh();
        }
        private void Refresh()
        {
            if (failed || dialog == null) return;
            try
            {
                string state = ScienceReportDialogAction.Preview(dialog.currentPage, out _, out var receiver);
                if (state == "DialogHidden") { if (row != null) row.SetActive(false); return; }
                if (row == null) Create();
                row!.SetActive(true);
                bool ready = state == "DialogReady";
                button.interactable = ready;
                led.color = ready ? new Color(.18f, .9f, .25f) : new Color(.13f, .28f, .14f);
                string detail = ready ? ScienceReportUI.Text("DialogReady", receiver!.Context) : ScienceReportUI.Text(state);
                label.text = ScienceReportUI.Text("SaveToManager") + "\n" + detail;
                tooltip.SetText(ScienceReportUI.Text("SaveToManager") + "\n" + detail);
            }
            catch (Exception error)
            {
                failed = true;
                if (row != null) row.SetActive(false);
                Debug.LogError("[Koffee] Optional Manager dialog button unavailable; stock controls remain available: " + error);
            }
        }
        private void Create()
        {
            var template = (Button)KeepButton.GetValue(dialog)!;
            var root = (RectTransform)dialog.transform;
            if (root.GetComponent<VerticalLayoutGroup>() == null || root.GetComponent<ContentSizeFitter>() == null)
                throw new InvalidOperationException("Stock result dialog no longer has its fitted vertical layout.");
            // Stock's centered pivot grows in both directions. Preserve its top
            // edge, then let the existing fitter grow/shrink only downwards.
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            root.GetWorldCorners(corners);
            Vector3 top = (corners[1] + corners[2]) * .5f;
            root.pivot = new Vector2(root.pivot.x, 1);
            root.GetWorldCorners(corners);
            root.position += top - (corners[1] + corners[2]) * .5f;

            row = new GameObject("Koffee Manager action", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var rect = (RectTransform)row.transform;
            rect.SetParent(root, false);
            rect.sizeDelta = new Vector2(root.rect.width, 62);
            var rowLayout = row.GetComponent<LayoutElement>();
            rowLayout.minHeight = rowLayout.preferredHeight = 62;
            rowLayout.flexibleHeight = 0;
            row.GetComponent<Image>().color = new Color(.12f, .14f, .16f, .98f);

            // Clone only the button, then replace its event list and graphics.
            // The original button and its persistent callbacks are never edited.
            button = Instantiate(template, rect, false);
            button.name = "Save stock report to Manager";
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(Save);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            foreach (var graphic in button.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            var buttonRect = (RectTransform)button.transform;
            buttonRect.anchorMin = Vector2.zero; buttonRect.anchorMax = Vector2.one;
            buttonRect.offsetMin = new Vector2(8, 7); buttonRect.offsetMax = new Vector2(-8, -7);
            var layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            var background = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
            background.enabled = true; background.sprite = null; background.overrideSprite = null;
            background.type = Image.Type.Simple; background.color = Color.white; background.raycastTarget = true;
            button.targetGraphic = background; button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1, .95f, .85f);
            colors.pressedColor = new Color(.8f, .83f, .85f); colors.disabledColor = new Color(.55f, .57f, .59f);
            colors.colorMultiplier = 1; button.colors = colors;
            // White casing, vertical orange identification stripe and green LED.
            var stripe = ImageChild("Orange stripe", buttonRect, new Color(1, .39f, .06f));
            stripe.rectTransform.anchorMin = new Vector2(0, 0); stripe.rectTransform.anchorMax = new Vector2(0, 1);
            stripe.rectTransform.pivot = new Vector2(0, .5f); stripe.rectTransform.sizeDelta = new Vector2(8, 0);
            var rim = ImageChild("LED rim", buttonRect, new Color(.15f, .18f, .19f));
            rim.rectTransform.anchorMin = rim.rectTransform.anchorMax = new Vector2(1, .5f);
            rim.rectTransform.anchoredPosition = new Vector2(-20, 0); rim.rectTransform.sizeDelta = new Vector2(14, 14);
            led = ImageChild("Green LED", rim.rectTransform, Color.white);
            led.rectTransform.anchorMin = Vector2.zero; led.rectTransform.anchorMax = Vector2.one;
            led.rectTransform.offsetMin = new Vector2(3, 3); led.rectTransform.offsetMax = new Vector2(-3, -3);

            var textObject = new GameObject("Manager action label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(buttonRect, false);
            label = textObject.GetComponent<TextMeshProUGUI>();
            var fontSource = dialog.GetComponentInChildren<TextMeshProUGUI>(true);
            label.font = fontSource.font; label.fontSharedMaterial = fontSource.fontSharedMaterial;
            label.fontSize = 14; label.color = new Color(.08f, .1f, .12f); label.raycastTarget = false;
            label.richText = false;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis;
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(18, 3); label.rectTransform.offsetMax = new Vector2(-38, -3);
            tooltip = button.GetComponent<TooltipController_Text>() ?? button.GetComponentInChildren<TooltipController_Text>(true);
            if (tooltip == null) throw new InvalidOperationException("Stock Keep button has no text tooltip template.");
            button.gameObject.SetActive(true);
        }
        private static Image ImageChild(string name, RectTransform parent, Color color)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(Image));
            child.transform.SetParent(parent, false);
            var image = child.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
            return image;
        }
        private void Save()
        {
            var page = dialog.currentPage;
            if (page == null || !ScienceReportDialogAction.Save(page)) { Refresh(); return; }
            // These notifications also accompany stock Keep. Observers run only
            // after a successful delivery and cannot roll it back or charge again.
            try { GameEvents.OnExperimentStored.Fire(page.pageData); }
            catch (Exception error) { Debug.LogError("[Koffee] Stored report observer failed: " + error); }
            try { GameEvents.OnROCExperimentStored.Fire(page.pageData); }
            catch (Exception error) { Debug.LogError("[Koffee] Stored ROC report observer failed: " + error); }
            // Remove exactly the committed page through stock's unsubscribe and
            // navigation path. Do not invoke OnKeepData to perform another action.
            try
            {
                if (dialog != null && ((List<ExperimentResultDialogPage>)Pages.GetValue(dialog)!).Contains(page))
                    RemovePage.Invoke(dialog, new object[] { page });
            }
            catch (Exception error) { Debug.LogError("[Koffee] Report stored; closing result page failed: " + error); }
        }
    }
}
