using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MagpieTrove.Services;

public static class PerceptualHash
{
	private const int Width = 9;

	private const int Height = 8;

	public static ulong Compute(BitmapSource source)
	{
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			drawingContext.DrawRectangle(Brushes.Black, null, new Rect(0.0, 0.0, 9.0, 8.0));
			drawingContext.DrawImage(source, new Rect(0.0, 0.0, 9.0, 8.0));
		}
		RenderOptions.SetBitmapScalingMode((DependencyObject)(object)drawingVisual, BitmapScalingMode.HighQuality);
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(9, 8, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual);
		int num = 36;
		byte[] array = new byte[num * 8];
		renderTargetBitmap.CopyPixels(array, num, 0);
		double[] array2 = new double[72];
		for (int i = 0; i < array2.Length; i++)
		{
			int num2 = i * 4;
			array2[i] = 0.114 * (double)(int)array[num2] + 0.587 * (double)(int)array[num2 + 1] + 0.299 * (double)(int)array[num2 + 2];
		}
		ulong num3 = 0uL;
		int num4 = 0;
		for (int j = 0; j < 8; j++)
		{
			for (int k = 0; k < 8; k++)
			{
				double num5 = array2[j * 9 + k];
				double num6 = array2[j * 9 + k + 1];
				if (num5 > num6)
				{
					num3 |= (ulong)(1L << num4);
				}
				num4++;
			}
		}
		return num3;
	}

	public static int Distance(ulong a, ulong b)
	{
		return BitOperations.PopCount(a ^ b);
	}
}
