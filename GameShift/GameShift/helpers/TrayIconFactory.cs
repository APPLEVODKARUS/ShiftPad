using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingIcon   = System.Drawing.Icon;
using DrawingBitmap = System.Drawing.Bitmap;

namespace GameShift.Helpers;

/// <summary>
/// Сборка иконки трея из WPF-геометрии. Рендерим программно,
/// чтобы не таскать .ico-файл и автоматически держать ту же палитру,
/// что и основной UI.
/// </summary>
internal static class TrayIconFactory
{
    private const int IconSize = 32;

    // Геометрия GamepadVariant из MaterialDesignIcons (24×24 viewBox).
    private const string GamepadGeometry =
        "M11,14H13V16H11V14M9,11H11V13H9V11M13,11H15V13H13V11" +
        "M11,8H13V10H11V8M21,4H3C1.89,4 1,4.89 1,6V18A2,2 0 0,0 3,20H21" +
        "A2,2 0 0,0 23,18V6C23,4.89 22.1,4 21,4M11,15H13V17H11V15" +
        "M14,8H10V12H14V8M19,15H21A2,2 0 0,0 23,13V11A2,2 0 0,0 21,9H19V15Z";

    /// <summary>
    /// Иконка геймпада в фирменном зелёном цвете. Возвращённый
    /// <see cref="DrawingIcon"/> владеет своими ресурсами и должен
    /// быть утилизирован вместе с TrayIcon.
    /// </summary>
    public static DrawingIcon CreateGamepadIcon()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var geom = Geometry.Parse(GamepadGeometry);

            // 24×24 → 32×32 с 4-пиксельным паддингом.
            const double pad   = 4.0;
            const double scale = (IconSize - 2 * pad) / 24.0;

            dc.PushTransform(new TranslateTransform(pad, pad));
            dc.PushTransform(new ScaleTransform(scale, scale));

            var fg = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0x6A));
            fg.Freeze();
            dc.DrawGeometry(fg, pen: null, geom);

            dc.Pop();
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;

        using var bmp = new DrawingBitmap(ms);

        // GetHicon() возвращает «сырой» HICON, который обязан быть
        // освобождён через DestroyIcon. Clone() копирует изображение
        // в независимый Icon — после этого хэндл можно убивать.
        var hicon = bmp.GetHicon();
        try
        {
            using var temp = DrawingIcon.FromHandle(hicon);
            return (DrawingIcon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
