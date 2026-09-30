// COMMANDER LEADS (the Academy, 2026-09-30): a toggle on the fleet screens that
// sends your commander at the head of the fleet you launch next — it fights
// harder by AcademySystem.CaptainBonus. Shown once the Academy is built.
using UnityEngine.UIElements;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class CaptainToggle
    {
        /// <summary>Armed for the next launch from the screen that showed the toggle.</summary>
        public static bool Want { get; private set; }

        /// <summary>The toggle row (null when there's no Academy yet). Disarmed each time it's built.</summary>
        public static VisualElement? Build(GameState s)
        {
            Want = false;
            if (AcademySystem.Level(s) < 1) return null;
            var can = AcademySystem.CanLead(s);
            string On() => $"✓ COMMANDER LEADS · +{System.Math.Round(AcademySystem.CaptainBonus(s) * 100)}%";
            string Off() => $"COMMANDER LEADS · +{System.Math.Round(AcademySystem.CaptainBonus(s) * 100)}%";
            Button? b = null;
            b = Widgets.TextButton(can.Ok ? Off() : $"COMMANDER · {can.Reason}", () =>
            {
                Want = !Want;
                b!.text = Want ? On() : Off();
                Widgets.SetButtonHighlight(b, Want);
            }, 10);
            b.style.marginTop = 6;
            b.style.height = 34;
            Widgets.SetButtonEnabled(b, can.Ok);
            return b;
        }

        /// <summary>After a launch: put the commander at its head if the toggle was on.</summary>
        public static void Apply(GameState s, int marchId)
        {
            if (Want && AcademySystem.Lead(s, marchId).Ok)
                UIController.Instance?.Toast("Your commander leads this fleet", Icon.Star, UiTheme.Energy);
            Want = false;
        }
    }
}
