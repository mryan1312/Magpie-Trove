using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MagpieTrove.Services;

public static class ModelInstallService
{
	public const string FileName = "clip-vit-b32-vision.onnx";

	public const string DownloadUrl = "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/vision_model.onnx?download=true";

	public const string ExpectedSha256 = "FD6E1402A588279D1723C7534D4BCBA5BC0B14B47DFAB0E46F8C47B8270D7D40";

	private static readonly HttpClient Client = new HttpClient
	{
		Timeout = Timeout.InfiniteTimeSpan
	};

	public static string ModelPath(string directory)
	{
		return Path.Combine(Path.GetFullPath(directory), "clip-vit-b32-vision.onnx");
	}

	public static async Task DownloadAsync(string directory, IProgress<ModelDownloadProgress>? progress = null, CancellationToken token = default(CancellationToken))
	{
		directory = Path.GetFullPath(directory);
		Directory.CreateDirectory(directory);
		string destination = ModelPath(directory);
		string temporary = destination + ".download";
		try
		{
			using HttpResponseMessage response = await Client.GetAsync("https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/vision_model.onnx?download=true", HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(continueOnCapturedContext: false);
			response.EnsureSuccessStatusCode();
			long? total = response.Content.Headers.ContentLength;
			await using (Stream source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(continueOnCapturedContext: false))
			{
				await using FileStream target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
				byte[] buffer = new byte[131072];
				long downloaded = 0L;
				while (true)
				{
					int read = await source.ReadAsync(buffer, token).ConfigureAwait(continueOnCapturedContext: false);
					if (read == 0)
					{
						break;
					}
					await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(continueOnCapturedContext: false);
					downloaded += read;
					progress?.Report(new ModelDownloadProgress(downloaded, total));
				}
				await target.FlushAsync(token).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (!(await HasExpectedHashAsync(temporary, "FD6E1402A588279D1723C7534D4BCBA5BC0B14B47DFAB0E46F8C47B8270D7D40", token).ConfigureAwait(continueOnCapturedContext: false)))
			{
				throw new InvalidDataException("The downloaded model did not match its published SHA-256 checksum.");
			}
			File.Move(temporary, destination, overwrite: true);
		}
		catch
		{
			try
			{
				if (File.Exists(temporary))
				{
					File.Delete(temporary);
				}
			}
			catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
			}
			throw;
		}
	}

	internal static async Task<bool> HasExpectedHashAsync(string path, string expectedHash, CancellationToken token = default(CancellationToken))
	{
		bool result;
		await using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
		{
			result = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(continueOnCapturedContext: false)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
		}
		return result;
	}
}
