using KSP.UI.Screens;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    [KSPAddon(KSPAddon.Startup.FlightAndEditor, false)]
    public sealed class KoffeeFlightUI : MonoBehaviour
    {
        private const string IconPath = "ShoAerospace/Koffee/Icons/KoffeeApp";
        private const string InputLockId = "ShoAerospace.Koffee.StatusWindow";
        private static KoffeeFlightUI? instance;

        private readonly KoffeeStatusWindow window = new KoffeeStatusWindow();
        private ApplicationLauncherButton? button;
        private Texture2D? fallbackIcon;
        private bool controlsLocked;
        private bool stockUiVisible = true;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }
            instance = this;
            GameEvents.onGUIApplicationLauncherReady.Add(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnLauncherDestroyed);
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);
            GameEvents.onVesselChange.Add(OnVesselChanged);
            GameEvents.onVesselCrewWasModified.Add(OnVesselChanged);
        }

        private void Start() => TryAddButton();

        private void Update()
        {
            TryAddButton();
            window.RefreshIfNeeded();
            UpdateInputLock();
        }

        private void OnGUI()
        {
            if (stockUiVisible && (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)) window.Draw();
        }

        private void OnLauncherReady() => TryAddButton();

        private void OnLauncherDestroyed()
        {
            button = null;
            window.Close();
            RemoveInputLock();
        }

        private void OnHideUI()
        {
            stockUiVisible = false;
            RemoveInputLock();
        }

        private void OnShowUI() => stockUiVisible = true;

        private void OnVesselChanged(Vessel vessel) => window.Invalidate();

        private void OpenWindow() => window.Open();

        private void CloseWindow()
        {
            window.Close();
            RemoveInputLock();
        }

        private void TryAddButton()
        {
            if (button != null || !(HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor) ||
                !ApplicationLauncher.Ready || ApplicationLauncher.Instance == null) return;
            Texture icon = GameDatabase.Instance.GetTexture(IconPath, false);
            if (icon == null)
            {
                if (fallbackIcon == null) fallbackIcon = CreateFallbackIcon();
                icon = fallbackIcon;
            }
            button = ApplicationLauncher.Instance.AddModApplication(
                OpenWindow, CloseWindow, null, null, null, null,
                ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW |
                ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH,
                icon);
            if (button == null) return;
            if (window.IsOpen) button.SetTrue(false);
            else button.SetFalse(false);
        }

        private void RemoveButton()
        {
            ApplicationLauncherButton? registered = button;
            button = null;
            if (registered != null && ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(registered);
        }

        private void UpdateInputLock()
        {
            if (!stockUiVisible || !window.IsOpen)
            {
                RemoveInputLock();
                return;
            }
            float scale = KoffeeStatusWindow.Scale;
            Vector3 mouse = Input.mousePosition;
            var point = new Vector2(mouse.x / scale, (Screen.height - mouse.y) / scale);
            bool hovered = window.WindowRect.Contains(point);
            if (hovered && !controlsLocked)
            {
                InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS |
                    ControlTypes.MAP_UI | ControlTypes.TWEAKABLES |
                    (HighLogic.LoadedSceneIsEditor ? ControlTypes.EDITOR_LOCK : (ControlTypes)0), InputLockId);
                controlsLocked = true;
            }
            else if (!hovered) RemoveInputLock();
        }

        private void RemoveInputLock()
        {
            if (!controlsLocked) return;
            InputLockManager.RemoveControlLock(InputLockId);
            controlsLocked = false;
        }

        private static Texture2D CreateFallbackIcon()
        {
            const int size = 38;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Koffee AppLauncher placeholder",
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            Color32 transparent = new Color32(0, 0, 0, 0);
            Color32 cup = new Color32(222, 215, 190, 255);
            Color32 coffee = new Color32(104, 67, 42, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = transparent;
            for (int y = 10; y <= 29; y++)
                for (int x = 7; x <= 27; x++) pixels[y * size + x] = cup;
            for (int y = 13; y <= 17; y++)
                for (int x = 9; x <= 25; x++) pixels[y * size + x] = coffee;
            for (int y = 14; y <= 25; y++)
                for (int x = 28; x <= 32; x++)
                    if (y <= 17 || y >= 22 || x >= 31) pixels[y * size + x] = cup;
            for (int x = 5; x <= 29; x++) pixels[31 * size + x] = cup;
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            GameEvents.onGUIApplicationLauncherReady.Remove(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnLauncherDestroyed);
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);
            GameEvents.onVesselChange.Remove(OnVesselChanged);
            GameEvents.onVesselCrewWasModified.Remove(OnVesselChanged);
            RemoveInputLock();
            RemoveButton();
            if (fallbackIcon != null) Destroy(fallbackIcon);
            fallbackIcon = null;
            instance = null;
        }
    }
}
