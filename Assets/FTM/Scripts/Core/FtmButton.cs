using UnityEngine;
using UnityEngine.UI;

namespace FTM
{
    public enum BtnStyle { Red, Dark, Green, Yellow }

    /// <summary>
    /// Flat square button: thin outline, solid fill. Hover / keyboard selection turns the outline white,
    /// pressing darkens the fill, disabled is dark gray with gray text. "Active" = selected tab/card (red).
    /// </summary>
    public class FtmButton : Button
    {
        public Image outlineImg;
        public Image fillImg;
        public Text label;
        public BtnStyle style = BtnStyle.Dark;
        public bool active;
        public bool redBorderWhenActive; // cards: red border instead of red fill
        public Color? customFill { get; set; }
        public Color? outlineOverride { get; set; }   // e.g. yellow = staged choice
        public bool activeOutlineOnly;                // active = white outline, keep the fill (speed buttons)

        SelectionState _state = SelectionState.Normal;

        public void SetActive(bool on) { active = on; Apply(); }
        public void SetEnabled(bool on) { interactable = on; Apply(); }
        public void SetLabel(string s) { if (label != null) label.text = s; }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            _state = state;
            Apply();
        }

        Color BaseFill()
        {
            if (customFill.HasValue) return customFill.Value;
            switch (style)
            {
                case BtnStyle.Red: return Theme.Red;
                case BtnStyle.Green: return Theme.Green;
                case BtnStyle.Yellow: return Theme.Yellow;
                default: return Theme.Panel;
            }
        }

        public void Apply()
        {
            if (fillImg == null || outlineImg == null) return;
            bool disabled = !IsInteractable();
            Color fill = BaseFill();
            Color outline = style == BtnStyle.Dark ? Theme.Outline : fill;
            Color text = (style == BtnStyle.Green || style == BtnStyle.Yellow) ? Theme.Dark : Theme.TextPrimary;
            if (customFill.HasValue) text = Theme.TextOn(customFill.Value);

            if (active && activeOutlineOnly) outline = Color.white;
            else if (active)
            {
                if (redBorderWhenActive) outline = Theme.Red;
                else { fill = Theme.Red; outline = Theme.Red; text = Theme.TextPrimary; }
            }

            if (disabled)
            {
                fill = Theme.DisabledFill; outline = Theme.DisabledFill; text = Theme.DisabledText;
                if (active && redBorderWhenActive) outline = Theme.Red;
            }
            else if (_state == SelectionState.Pressed)
            {
                fill = Theme.Darken(fill, 0.7f);
                outline = Color.white;
            }
            else if (_state == SelectionState.Highlighted || _state == SelectionState.Selected)
            {
                outline = Color.white;
            }

            if (outlineOverride.HasValue && !disabled) outline = outlineOverride.Value;
            fillImg.color = fill;
            outlineImg.color = outline;
            if (label != null) label.color = text;
        }
    }
}
