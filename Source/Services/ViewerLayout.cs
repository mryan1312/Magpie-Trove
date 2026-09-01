using System;

namespace MagpieTrove.Services;

public static class ViewerLayout
{
	public static ViewerPlacement Fit(int pixelWidth, int pixelHeight, double viewportWidth, double viewportHeight)
	{
		if (pixelWidth <= 0 || pixelHeight <= 0 || viewportWidth <= 0.0 || viewportHeight <= 0.0)
		{
			return new ViewerPlacement(1.0, 0.0, 0.0);
		}
		double num = Math.Min(1.0, Math.Min(viewportWidth / (double)pixelWidth, viewportHeight / (double)pixelHeight));
		return new ViewerPlacement(num, (viewportWidth - (double)pixelWidth * num) / 2.0, (viewportHeight - (double)pixelHeight * num) / 2.0);
	}

	public static ViewerPlacement ZoomAround(double oldScale, double oldLeft, double oldTop, double newScale, double centerX, double centerY)
	{
		if (oldScale <= 0.0)
		{
			oldScale = 1.0;
		}
		double num = (centerX - oldLeft) / oldScale;
		double num2 = (centerY - oldTop) / oldScale;
		return new ViewerPlacement(newScale, centerX - num * newScale, centerY - num2 * newScale);
	}
}
