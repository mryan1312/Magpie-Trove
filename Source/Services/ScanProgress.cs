namespace MagpieTrove.Services;

public sealed record ScanProgress(string Message, int Processed, int Total)
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
}
