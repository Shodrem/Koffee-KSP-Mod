using System;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal static class StationPersonnelSelector
    {
        internal static void Refresh(BaseField field, string[] options,
            string[] display, bool editable)
        {
            if (!(field?.uiControlFlight is UI_ChooseOption control)) return;
            bool changed = control.options == null || control.display == null ||
                !control.options.SequenceEqual(options, StringComparer.Ordinal) ||
                !control.display.SequenceEqual(display, StringComparer.Ordinal);
            control.controlEnabled = editable && options.Length > 1;
            if (!changed) return;
            control.options = options;
            control.display = display;
            // The PAW may already be open when crew board, leave or become busy.
            if (control.partActionItem != null && control.partActionItem.Window != null)
                control.partActionItem.Window.displayDirty = true;
        }
    }
}
