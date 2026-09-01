namespace MagpieTrove.Services;

public sealed record ModelDownloadProgress(long DownloadedBytes, long? TotalBytes)
{
	public double Percent
	{
		get
		{
			long? totalBytes = TotalBytes;
			if (!totalBytes.HasValue || totalBytes.GetValueOrDefault() <= 0)
			{
				return 0.0;
			}
			return (double)DownloadedBytes * 100.0 / (double)TotalBytes.Value;
		}
	}
}
