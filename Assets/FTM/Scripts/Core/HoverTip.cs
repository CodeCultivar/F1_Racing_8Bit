using UnityEngine;
using UnityEngine.EventSystems;

namespace FTM
{
    /// <summary>Shows the full text of a shortened label while hovered.</summary>
    public class HoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string full;
        public void OnPointerEnter(PointerEventData e) { if (GameApp.I != null) GameApp.I.ShowTooltip(full, (RectTransform)transform); }
        public void OnPointerExit(PointerEventData e) { if (GameApp.I != null) GameApp.I.HideTooltip(); }
        void OnDisable() { if (GameApp.I != null) GameApp.I.HideTooltip(); }
    }
}
