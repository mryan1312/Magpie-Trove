namespace MagpieTrove.Services;

public sealed record ProbeResult(long TagId, string TagName, int Positives, int Negatives, double Quality, string Message)
{
	public bool Trained
	{
		get
		{
			if (Positives > 0)
			{
				return Negatives > 0;
			}
			return false;
		}
	}
}
