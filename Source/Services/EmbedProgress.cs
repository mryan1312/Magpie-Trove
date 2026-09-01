using System;

namespace MagpieTrove.Services;

public sealed record EmbedProgress(int Processed, int Total, double ImagesPerSecond)
{
	public double Percent
	{
		get
		{
			if (Total > 0)
			{
				return (double)Processed * 100.0 / (double)Total;
			}
			return 0.0;
		}
	}

	public TimeSpan Remaining
	{
		get
		{
			if (!(ImagesPerSecond <= 0.01))
			{
				return TimeSpan.FromSeconds((double)(Total - Processed) / ImagesPerSecond);
			}
			return TimeSpan.Zero;
		}
	}
}
