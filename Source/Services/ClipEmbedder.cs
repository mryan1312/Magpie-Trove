using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MagpieTrove.Services;

public sealed class ClipEmbedder : IDisposable
{
	public const string ModelId = "clip-vit-b32-vision";

	private const int InputSize = 224;

	private static readonly float[] Mean = new float[3] { 0.48145467f, 0.4578275f, 0.40821072f };

	private static readonly float[] Std = new float[3]
	{
		0.26862955f,
		0.2613026f,
		0.27577711f
	};

	private readonly InferenceSession _session;

	private readonly string _inputName;

	private readonly string _outputName;

	public int Dimensions { get; }

	public string Provider { get; }

	public static string DefaultModelPath => Path.Combine(AppSettingsService.Load().ModelDirectory, "clip-vit-b32-vision.onnx");

	public static bool IsModelAvailable => File.Exists(DefaultModelPath);

	public ClipEmbedder(string? modelPath = null, bool useGpu = true)
	{
		if (modelPath == null)
		{
			modelPath = DefaultModelPath;
		}
		if (!File.Exists(modelPath))
		{
			throw new FileNotFoundException("CLIP vision model not found.", modelPath);
		}
		static SessionOptions NewOptions() => new SessionOptions
		{
			GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
		};

		InferenceSession? session = null;
		Provider = "CPU";
		if (useGpu)
		{
			// DirectML can fail either when the provider is registered — Windows
			// before 1903 has no DirectML.dll, and the runtime does not ship one —
			// or only once the session is built, on an unsupported or broken
			// driver. Both steps therefore sit inside the fallback.
			try
			{
				SessionOptions gpuOptions = NewOptions();
				gpuOptions.AppendExecutionProvider_DML();
				session = new InferenceSession(modelPath, gpuOptions);
				Provider = "GPU (DirectML)";
			}
			catch (Exception)
			{
				session = null;
				Provider = "CPU";
			}
		}
		_session = session ?? new InferenceSession(modelPath, NewOptions());
		_inputName = _session.InputMetadata.Keys.First();
		IReadOnlyDictionary<string, NodeMetadata> outputMetadata = _session.OutputMetadata;
		_outputName = outputMetadata.Keys.FirstOrDefault((string k) => k.Equals("image_embeds", StringComparison.OrdinalIgnoreCase)) ?? outputMetadata.Keys.FirstOrDefault((string k) => k.Equals("pooler_output", StringComparison.OrdinalIgnoreCase)) ?? outputMetadata.Keys.First();
		int[] dimensions = outputMetadata[_outputName].Dimensions;
		Dimensions = ((dimensions.Length != 0 && dimensions[^1] > 0) ? dimensions[^1] : 512);
	}

	public string Describe()
	{
		return $"input='{_inputName}' output='{_outputName}' dim={Dimensions} [outputs: {string.Join(", ", _session.OutputMetadata.Keys)}]";
	}

	public float[][] Embed(IReadOnlyList<BitmapSource> images)
	{
		if (images.Count == 0)
		{
			return Array.Empty<float[]>();
		}
		DenseTensor<float> denseTensor = new DenseTensor<float>([images.Count, 3, 224, 224]);
		for (int i = 0; i < images.Count; i++)
		{
			WriteTensor(images[i], denseTensor, i);
		}
		using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> source = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, denseTensor)], [_outputName]);
		Tensor<float> tensor = source.First().AsTensor<float>();
		int num;
		if (tensor.Dimensions.Length <= 1)
		{
			num = Dimensions;
		}
		else
		{
			ReadOnlySpan<int> dimensions = tensor.Dimensions;
			num = dimensions[dimensions.Length - 1];
		}
		int num2 = num;
		float[][] array = new float[images.Count][];
		for (int j = 0; j < images.Count; j++)
		{
			float[] array2 = new float[num2];
			for (int k = 0; k < num2; k++)
			{
				array2[k] = tensor[new int[2] { j, k }];
			}
			Normalize(array2);
			array[j] = array2;
		}
		return array;
	}

	public float[] Embed(BitmapSource image)
	{
		return Embed([image])[0];
	}

	private static void WriteTensor(BitmapSource source, DenseTensor<float> tensor, int index)
	{
		double num = 224.0 / (double)Math.Min(source.PixelWidth, source.PixelHeight);
		double num2 = (double)source.PixelWidth * num;
		double num3 = (double)source.PixelHeight * num;
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			drawingContext.DrawRectangle(Brushes.Black, null, new Rect(0.0, 0.0, 224.0, 224.0));
			drawingContext.DrawImage(source, new Rect((224.0 - num2) / 2.0, (224.0 - num3) / 2.0, num2, num3));
		}
		RenderOptions.SetBitmapScalingMode((DependencyObject)(object)drawingVisual, BitmapScalingMode.HighQuality);
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(224, 224, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual);
		int num4 = 896;
		byte[] array = new byte[num4 * 224];
		renderTargetBitmap.CopyPixels(array, num4, 0);
		for (int i = 0; i < 224; i++)
		{
			int num5 = i * num4;
			for (int j = 0; j < 224; j++)
			{
				int num6 = num5 + j * 4;
				tensor[new int[4] { index, 0, i, j }] = ((float)(int)array[num6 + 2] / 255f - Mean[0]) / Std[0];
				tensor[new int[4] { index, 1, i, j }] = ((float)(int)array[num6 + 1] / 255f - Mean[1]) / Std[1];
				tensor[new int[4] { index, 2, i, j }] = ((float)(int)array[num6] / 255f - Mean[2]) / Std[2];
			}
		}
	}

	public static void Normalize(float[] vector)
	{
		double num = 0.0;
		foreach (float num2 in vector)
		{
			num += (double)num2 * (double)num2;
		}
		double num3 = Math.Sqrt(num);
		if (!(num3 < 1E-09))
		{
			float num4 = (float)(1.0 / num3);
			for (int j = 0; j < vector.Length; j++)
			{
				vector[j] *= num4;
			}
		}
	}

	public void Dispose()
	{
		_session.Dispose();
	}
}
