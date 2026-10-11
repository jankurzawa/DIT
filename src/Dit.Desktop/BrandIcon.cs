using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dit.Desktop;

internal static class BrandIcon
{
    public static void Write(string path)
    {
        int[] sizes=[16,20,24,32,40,48,64,128,256];
        var images=new byte[sizes.Length][];
        for(var i=0;i<sizes.Length;i++)
        {
            var size=sizes[i];var visual=new DrawingVisual();
            using(var drawing=visual.RenderOpen())
            {
                drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(246,248,246)),new Pen(new SolidColorBrush(Color.FromRgb(207,217,211)),size/128d),new Rect(1,1,size-2,size-2),size*.18,size*.18);
                var text=new FormattedText("DIT",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.Black,FontStretches.Normal),size*.43,Brushes.Black,1);
                drawing.DrawText(text,new Point((size-text.Width)/2,(size-text.Height)/2-size*.035));
                drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(57,192,160)),null,new Rect(size*.19,size*.76,size*.62,size*.045),size*.02,size*.02);
            }
            var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();png.Save(stream);images[i]=stream.ToArray();
            if(size==256)File.WriteAllBytes(Path.ChangeExtension(path,"png"),images[i]);
        }
        using var output=new BinaryWriter(File.Create(path));output.Write((ushort)0);output.Write((ushort)1);output.Write((ushort)sizes.Length);
        var offset=6+sizes.Length*16;
        for(var i=0;i<sizes.Length;i++)
        {
            output.Write((byte)(sizes[i]==256?0:sizes[i]));output.Write((byte)(sizes[i]==256?0:sizes[i]));output.Write((byte)0);output.Write((byte)0);output.Write((ushort)1);output.Write((ushort)32);output.Write(images[i].Length);output.Write(offset);offset+=images[i].Length;
        }
        foreach(var png in images)output.Write(png);
    }
}
