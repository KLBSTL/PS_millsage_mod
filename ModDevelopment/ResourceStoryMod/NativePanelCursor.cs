using UnityEngine;

namespace ResourceStoryMod
{
    internal static class NativePanelCursor
    {
        internal static bool PanelOpen
        {
            get
            {
                var core=Core.Instance;var host=core?.Story;var economy=host?.Economy;
                return host?.Visible==true || host?.Campaign?.PanelOpen==true || economy?.LedgerOpen==true
                    || economy?.Business?.PanelOpen==true || economy?.Workshop?.PanelOpen==true
                    || economy?.PackageB?.PanelOpen==true || economy?.Trade?.PanelOpen==true
                    || core?.Test?.Tools?.PanelOpen==true;
            }
        }

        internal static void Acquire(StoreUIManager ui)
        {
            // Native IncrementMouseFlag also hides and center-locks the OS cursor.
            // Preserve its interaction counter, but use a free pointer for IMGUI.
            ui.mouseLockFlag++;
            FreePointer();
        }

        internal static void Release(StoreUIManager ui)
        {
            ui.mouseLockFlag--;
            if(PanelOpen)FreePointer();else ui.OnMouseFlagChanged();
        }

        internal static void Refresh(){if(PanelOpen)FreePointer();}

        private static void FreePointer()
        {
            if(Cursor.lockState!=CursorLockMode.None)Cursor.lockState=CursorLockMode.None;
            if(!Cursor.visible)Cursor.visible=true;
        }
    }
}
