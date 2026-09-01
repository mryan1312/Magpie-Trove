using System;
using System.Collections.Generic;
using System.Numerics.Tensors;
using System.Threading;
using MagpieTrove.Data;

namespace MagpieTrove.Services;

public sealed class VectorIndex
{
	private VectorSet _vectors = VectorSet.Empty(0);

	private Dictionary<long, int> _positions = new Dictionary<long, int>();

	private readonly Lock _gate = new Lock();

	public string Model { get; }

	public int Count
	{
		get
		{
			using (_gate.EnterScope())
			{
				return _vectors.Count;
			}
		}
	}

	public int Dimensions
	{
		get
		{
			using (_gate.EnterScope())
			{
				return _vectors.Dimensions;
			}
		}
	}

	public VectorIndex(string model)
	{
		Model = model;
	}

	public void Reload()
	{
		VectorSet vectorSet = EmbeddingRepository.LoadAll(Model);
		Dictionary<long, int> dictionary = new Dictionary<long, int>(vectorSet.Count);
		for (int i = 0; i < vectorSet.Count; i++)
		{
			dictionary[vectorSet.ImageIds[i]] = i;
		}
		using (_gate.EnterScope())
		{
			_vectors = vectorSet;
			_positions = dictionary;
		}
	}

	public bool TryGetVector(long imageId, out float[] vector)
	{
		using (_gate.EnterScope())
		{
			if (_positions.TryGetValue(imageId, out var value))
			{
				vector = _vectors[value].ToArray();
				return true;
			}
		}
		vector = Array.Empty<float>();
		return false;
	}

	public List<ScoredImage> Search(ReadOnlySpan<float> query, int count, IReadOnlySet<long>? candidates = null, IReadOnlySet<long>? exclude = null, float minimumScore = float.NegativeInfinity)
	{
		using (_gate.EnterScope())
		{
			VectorSet vectors = _vectors;
			if (vectors.Count == 0 || query.Length != vectors.Dimensions)
			{
				return new List<ScoredImage>();
			}
			PriorityQueue<long, float> priorityQueue = new PriorityQueue<long, float>(count + 1);
			for (int i = 0; i < vectors.Count; i++)
			{
				long num = vectors.ImageIds[i];
				if ((exclude != null && exclude.Contains(num)) || (candidates != null && !candidates.Contains(num)))
				{
					continue;
				}
				float num2 = TensorPrimitives.Dot(query, vectors[i]);
				if (!(num2 < minimumScore))
				{
					long element;
					float priority;
					if (priorityQueue.Count < count)
					{
						priorityQueue.Enqueue(num, num2);
					}
					else if (priorityQueue.TryPeek(out element, out priority) && num2 > priority)
					{
						priorityQueue.Enqueue(num, num2);
						priorityQueue.Dequeue();
					}
				}
			}
			List<ScoredImage> list = new List<ScoredImage>(priorityQueue.Count);
			long element2;
			float priority2;
			while (priorityQueue.TryDequeue(out element2, out priority2))
			{
				list.Add(new ScoredImage(element2, priority2));
			}
			list.Reverse();
			return list;
		}
	}

	public List<ScoredImage> SearchSimilarTo(long imageId, int count, IReadOnlySet<long>? candidates = null)
	{
		if (!TryGetVector(imageId, out float[] vector))
		{
			return new List<ScoredImage>();
		}
		return Search(vector, count, candidates, new HashSet<long> { imageId });
	}

	public float[] Centroid(IEnumerable<long> imageIds)
	{
		using (_gate.EnterScope())
		{
			if (_vectors.Dimensions == 0)
			{
				return Array.Empty<float>();
			}
			float[] array = new float[_vectors.Dimensions];
			int num = 0;
			foreach (long imageId in imageIds)
			{
				if (_positions.TryGetValue(imageId, out var value))
				{
					TensorPrimitives.Add(array, _vectors[value], array);
					num++;
				}
			}
			if (num == 0)
			{
				return Array.Empty<float>();
			}
			ClipEmbedder.Normalize(array);
			return array;
		}
	}

	public List<ScoredImage> Score(ReadOnlySpan<float> weights, float bias, int count, IReadOnlySet<long>? exclude = null)
	{
		using (_gate.EnterScope())
		{
			VectorSet vectors = _vectors;
			if (vectors.Count == 0 || weights.Length != vectors.Dimensions)
			{
				return new List<ScoredImage>();
			}
			PriorityQueue<long, float> priorityQueue = new PriorityQueue<long, float>(count + 1);
			for (int i = 0; i < vectors.Count; i++)
			{
				long num = vectors.ImageIds[i];
				if (exclude == null || !exclude.Contains(num))
				{
					float num2 = TensorPrimitives.Dot(weights, vectors[i]) + bias;
					long element;
					float priority;
					if (priorityQueue.Count < count)
					{
						priorityQueue.Enqueue(num, num2);
					}
					else if (priorityQueue.TryPeek(out element, out priority) && num2 > priority)
					{
						priorityQueue.Enqueue(num, num2);
						priorityQueue.Dequeue();
					}
				}
			}
			List<ScoredImage> list = new List<ScoredImage>(priorityQueue.Count);
			long element2;
			float priority2;
			while (priorityQueue.TryDequeue(out element2, out priority2))
			{
				list.Add(new ScoredImage(element2, priority2));
			}
			list.Reverse();
			return list;
		}
	}

	public long[] AllIds()
	{
		using (_gate.EnterScope())
		{
			return (long[])_vectors.ImageIds.Clone();
		}
	}
}
