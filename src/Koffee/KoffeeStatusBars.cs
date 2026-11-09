using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class KoffeeStatusBars
    {
        private const string TextureDirectory = "ShoAerospace/Koffee/UI/DeadlyMode/";
        private const float Width = 125;
        private const float Height = 20;

        internal static void Health(KoffeeCrewStatus crew)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(Height));
            Draw("deadly_progress", crew.HealthFraction);
            if (crew.IsKaffeinated)
            {
                Texture2D icon = GameDatabase.Instance.GetTexture("ShoAerospace/Koffee/UI/KoffeeStatus", false);
                if (icon != null)
                {
                    Rect rect = GUILayoutUtility.GetRect(Height, Height, GUILayout.ExpandWidth(false));
                    Color previousColor = GUI.color;
                    try
                    {
                        GUI.color = Color.white;
                        GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
                    }
                    finally { GUI.color = previousColor; }
                }
            }
            GUILayout.EndHorizontal();
        }

        internal static void Komfort(KomfortStatus status, GUIStyle style)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Localizer.Format("#Koffee_Crew_Komfort", KoffeeCrewDisplay.Komfort(status)),
                style, GUILayout.ExpandWidth(false));
            Draw("komfort_bar", status.Value);
            GUILayout.EndHorizontal();
        }

        internal static void Air(double? fraction, bool lastKnown, GUIStyle style)
        {
            if (!fraction.HasValue) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label(Localizer.Format("#Koffee_Crew_AirQuality"), style, GUILayout.ExpandWidth(false));
            Draw("air_quality", fraction);
            if (lastKnown)
                GUILayout.Label(Localizer.Format("#Koffee_Crew_AirLastKnown"), style, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
        }

        internal static void Draw(string textureName, double? fraction)
        {
            if (!fraction.HasValue) return;
            Texture2D texture = GameDatabase.Instance.GetTexture(TextureDirectory + textureName, false);
            if (texture == null) return;

            Rect track = GUILayoutUtility.GetRect(Width, Height, GUILayout.ExpandWidth(false));
            if (Event.current.type != EventType.Repaint) return;
            float fill = Mathf.Clamp01((float)fraction.Value);
            if (fill <= 0) return;
            GUI.DrawTextureWithTexCoords(new Rect(track.x, track.y, track.width * fill, track.height),
                texture, new Rect(0, 0, fill, 1));
        }
    }
}
