using System.Windows;
using System.Windows.Controls.Primitives;

namespace LLPlayer.Controls;

public static class SubtitlePopupPlacement
{
    public static CustomPopupPlacement[] Candidates(Size popup, Size target, bool below)
    {
        double x = (target.Width - popup.Width) / 2;
        var above = new CustomPopupPlacement(new Point(x, -popup.Height - 6), PopupPrimaryAxis.Horizontal);
        var under = new CustomPopupPlacement(new Point(x, target.Height + 6), PopupPrimaryAxis.Horizontal);
        return below ? [under, above] : [above, under];
    }
}
