using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

public static class ImageExtensions
{
    public static void DrawAttribution(this Image<Rgba32> image, string text)
    {
        var font = SystemFonts.CreateFont("Arial", 12);

        var options = new RichTextOptions(font)
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Origin = new PointF(image.Width - 10, image.Height - 10)
        };

        image.Mutate(ctx =>
        {
            var shadowOptions = new RichTextOptions(options)
            {
                Origin = new PointF(options.Origin.X + 2, options.Origin.Y + 2)
            };
            ctx.DrawText(shadowOptions, text, Color.Black);
            ctx.DrawText(options, text, Color.White);
        });
    }
}