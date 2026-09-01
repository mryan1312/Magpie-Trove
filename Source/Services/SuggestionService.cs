using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using MagpieTrove.Data;
using MagpieTrove.Models;

namespace MagpieTrove.Services;

public sealed class SuggestionService
{
	private readonly VectorIndex _index;

	public SuggestionService(VectorIndex index)
	{
		_index = index;
	}

	public List<TagSuggestion> SuggestFromNeighbours(IReadOnlyList<long> selection, int neighbourCount = 25, int max = 8)
	{
		if (selection.Count == 0 || _index.Count == 0)
		{
			return new List<TagSuggestion>();
		}
		float[] array = ((selection.Count == 1 && _index.TryGetVector(selection[0], out float[] vector)) ? vector : _index.Centroid(selection));
		if (array.Length == 0)
		{
			return new List<TagSuggestion>();
		}
		HashSet<long> taggedImageIds = TagRepository.GetTaggedImageIds();
		if (taggedImageIds.Count == 0)
		{
			return new List<TagSuggestion>();
		}
		HashSet<long> exclude = selection.ToHashSet();
		List<ScoredImage> list = _index.Search(array, neighbourCount, taggedImageIds, exclude);
		if (list.Count == 0)
		{
			return new List<TagSuggestion>();
		}
		Dictionary<long, List<long>> tagIdsForImages = TagRepository.GetTagIdsForImages(list.Select((ScoredImage n) => n.ImageId).ToList());
		Dictionary<long, double> dictionary = new Dictionary<long, double>();
		double totalWeight = 0.0;
		foreach (ScoredImage item in list)
		{
			double num = Math.Max(0.0, (double)(item.Score - list[list.Count - 1].Score) + 0.05);
			totalWeight += num;
			if (!tagIdsForImages.TryGetValue(item.ImageId, out var value))
			{
				continue;
			}
			foreach (long item2 in value)
			{
				dictionary[item2] = dictionary.GetValueOrDefault(item2) + num;
			}
		}
		if (totalWeight <= 0.0)
		{
			return new List<TagSuggestion>();
		}
		HashSet<long> existing = (from c in TagRepository.GetTagsForSelection(selection)
			select c.Id).ToHashSet();
		Dictionary<long, string> names = TagRepository.GetAllWithCounts().ToDictionary((TagItem t) => t.Id, (TagItem t) => t.Name);
		return (from kv in dictionary
			where !existing.Contains(kv.Key) && names.ContainsKey(kv.Key)
			select new TagSuggestion(kv.Key, names[kv.Key], kv.Value / totalWeight, "similar images") into s
			where s.Score >= 0.15
			orderby s.Score descending
			select s).Take(max).ToList();
	}

	public ProbeResult TrainProbe(long tagId, string tagName)
	{
		HashSet<long> positiveIds = TagRepository.GetImageIdsWithTag(tagId);
		List<long> list = (from id in TagRepository.GetTaggedImageIds()
			where !positiveIds.Contains(id)
			select id).ToList();
		List<float[]> list2 = Gather(positiveIds);
		if (list2.Count < 8)
		{
			return new ProbeResult(tagId, tagName, list2.Count, 0, 0.0, $"Needs at least 8 tagged examples with embeddings — has {list2.Count}.");
		}
		Random random = new Random(17);
		int wanted = Math.Min(list.Count, Math.Max(list2.Count * 3, 50));
		List<float[]> list3 = SelectNegatives(list2, list, wanted, random);
		if (list3.Count < 8)
		{
			return new ProbeResult(tagId, tagName, list2.Count, list3.Count, 0.0, "Needs more images tagged with something else to learn against.");
		}
		int dimensions = list2[0].Length;
		(double Quality, List<(float[] Weights, float Bias)> Models) tuple = CrossValidate(list2, list3, dimensions);
		double item = tuple.Quality;
		List<(float[], float)> item2 = tuple.Models;
		List<float[]> x = list2.Concat(list3).ToList();
		List<float> y = list2.Select((float[] _) => 1f).Concat(list3.Select((float[] _) => 0f)).ToList();
		var (array, num) = FitLogistic(x, y, dimensions);
		if (item2.Count > 0)
		{
			foreach (var (array2, num2) in item2)
			{
				TensorPrimitives.Add(array, array2, array);
				num += num2;
			}
			int num3 = item2.Count + 1;
			TensorPrimitives.Divide(array, num3, array);
			num /= (float)num3;
		}
		SaveProbe(tagId, array, num, list2.Count, list3.Count, item);
		string text = ((item >= 0.9) ? "should rank new matches very well" : ((item >= 0.75) ? "should rank new matches well" : ((!(item >= 0.6)) ? "no better than guessing; this tag may not be visually distinctive" : "weak — tag more examples to sharpen it")));
		string value = text;
		return new ProbeResult(tagId, tagName, list2.Count, list3.Count, item, $"Learned \"{tagName}\" from {list2.Count} tagged and {list3.Count} contrasting images — {value} ({item:P0}).");
	}

	private List<float[]> SelectNegatives(List<float[]> positives, List<long> candidateIds, int wanted, Random random)
	{
		List<(long Id, float[] Vector)> list = new List<(long Id, float[] Vector)>();
		foreach (long candidateId in candidateIds)
		{
			if (_index.TryGetVector(candidateId, out float[] vector) && vector.Any((float v) => v != 0f))
			{
				list.Add((candidateId, vector));
			}
		}
		if (list.Count <= wanted)
		{
			return list.Select<(long Id, float[] Vector), float[]>(((long Id, float[] Vector) p) => p.Vector).ToList();
		}
		float[] centre = new float[positives[0].Length];
		foreach (float[] positive in positives)
		{
			TensorPrimitives.Add(centre, positive, centre);
		}
		ClipEmbedder.Normalize(centre);
		List<(float[] Vector, float Similarity)> source = (from p in list
			select (Vector: p.Vector, Similarity: TensorPrimitives.Dot(centre, p.Vector)) into p
			orderby p.Similarity descending
			select p).ToList();
		int num = wanted / 2;
		List<float[]> list2 = (from p in source.Take(num)
			select p.Vector).ToList();
		list2.AddRange(from p in (from _ in source.Skip(num)
				orderby random.Next()
				select _).Take(wanted - num)
			select p.Vector);
		return list2;
	}

	private List<float[]> Gather(IEnumerable<long> ids)
	{
		List<float[]> list = new List<float[]>();
		foreach (long id in ids)
		{
			if (_index.TryGetVector(id, out float[] vector) && vector.Any((float v) => v != 0f))
			{
				list.Add(vector);
			}
		}
		return list;
	}

	private static (double Quality, List<(float[] Weights, float Bias)> Models) CrossValidate(List<float[]> positives, List<float[]> negatives, int dimensions, int folds = 5)
	{
		folds = Math.Max(2, Math.Min(folds, Math.Min(positives.Count, negatives.Count)));
		List<double> list = new List<double>();
		List<(float[], float)> list2 = new List<(float[], float)>();
		for (int i = 0; i < folds; i++)
		{
			List<float[]> list3 = new List<float[]>();
			List<float[]> list4 = new List<float[]>();
			List<float> list5 = new List<float>();
			List<float> list6 = new List<float>();
			for (int j = 0; j < positives.Count; j++)
			{
				(List<float[]>, List<float>) obj = ((j % folds == i) ? (list4, list6) : (list3, list5));
				obj.Item1.Add(positives[j]);
				obj.Item2.Add(1f);
			}
			for (int k = 0; k < negatives.Count; k++)
			{
				(List<float[]>, List<float>) obj2 = ((k % folds == i) ? (list4, list6) : (list3, list5));
				obj2.Item1.Add(negatives[k]);
				obj2.Item2.Add(0f);
			}
			if (list6.Any((float v) => v > 0.5f) && list6.Any((float v) => v < 0.5f))
			{
				var (array, num) = FitLogistic(list3, list5, dimensions);
				list.Add(AreaUnderCurve(array, num, list4, list6));
				list2.Add((array, num));
			}
		}
		return (Quality: (list.Count == 0) ? 0.0 : list.Average(), Models: list2);
	}

	private static (float[] Weights, float Bias) FitLogistic(List<float[]> x, List<float> y, int dimensions, int iterations = 400, float learningRate = 0.5f, float l2 = 0.01f)
	{
		float[] array = new float[dimensions];
		float num = 0f;
		int count = x.Count;
		int num2 = y.Count((float v) => v > 0.5f);
		int num3 = count - num2;
		float num4 = ((num2 > 0) ? ((float)count / (2f * (float)num2)) : 1f);
		float num5 = ((num3 > 0) ? ((float)count / (2f * (float)num3)) : 1f);
		float[] array2 = new float[dimensions];
		for (int num6 = 0; num6 < iterations; num6++)
		{
			Array.Clear(array2);
			float num7 = 0f;
			for (int num8 = 0; num8 < count; num8++)
			{
				float num9 = TensorPrimitives.Dot(array.AsSpan(), x[num8].AsSpan()) + num;
				float num10 = 1f / (1f + MathF.Exp(0f - num9));
				float num11 = ((y[num8] > 0.5f) ? num4 : num5);
				float num12 = (num10 - y[num8]) * num11;
				TensorPrimitives.AddMultiply(array2, x[num8], num12, array2);
				num7 += num12;
			}
			float num13 = learningRate / (float)count;
			for (int num14 = 0; num14 < dimensions; num14++)
			{
				array[num14] -= num13 * (array2[num14] + l2 * array[num14]);
			}
			num -= num13 * num7;
		}
		return (Weights: array, Bias: num);
	}

	private static double AreaUnderCurve(float[] weights, float bias, List<float[]> x, List<float> y)
	{
		if (x.Count == 0)
		{
			return 0.0;
		}
		List<(double, bool)> list = new List<(double, bool)>(x.Count);
		for (int i = 0; i < x.Count; i++)
		{
			float num = TensorPrimitives.Dot(weights.AsSpan(), x[i].AsSpan()) + bias;
			list.Add((num, y[i] > 0.5f));
		}
		int num2 = list.Count<(double, bool)>(((double Score, bool Positive) s) => s.Positive);
		int num3 = list.Count - num2;
		if (num2 == 0 || num3 == 0)
		{
			return 0.0;
		}
		list.Sort(((double Score, bool Positive) a, (double Score, bool Positive) b) => a.Score.CompareTo(b.Score));
		double num4 = 0.0;
		int num5 = 0;
		while (num5 < list.Count)
		{
			int num6;
			for (num6 = num5; num6 + 1 < list.Count && list[num6 + 1].Item1 == list[num5].Item1; num6++)
			{
			}
			double num7 = (double)(num5 + num6) / 2.0 + 1.0;
			for (int num8 = num5; num8 <= num6; num8++)
			{
				if (list[num8].Item2)
				{
					num4 += num7;
				}
			}
			num5 = num6 + 1;
		}
		return (num4 - (double)(num2 * (num2 + 1)) / 2.0) / ((double)num2 * (double)num3);
	}

	public List<ScoredImage> FindCandidates(long tagId, int count = 200)
	{
		(float[], float)? tuple = LoadProbe(tagId);
		if (!tuple.HasValue)
		{
			return new List<ScoredImage>();
		}
		HashSet<long> imageIdsWithTag = TagRepository.GetImageIdsWithTag(tagId);
		return (from s in _index.Score(tuple.Value.Item1, tuple.Value.Item2, count, imageIdsWithTag)
			where s.Score > 0f
			select s).ToList();
	}

	public bool HasProbe(long tagId)
	{
		return LoadProbe(tagId).HasValue;
	}

	private static void SaveProbe(long tagId, float[] weights, float bias, int positives, int negatives, double accuracy)
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "INSERT INTO tag_probes (tag_id, model, dim, weights, bias, positives, negatives, accuracy, trained_at)\nVALUES ($tag, $model, $dim, $w, $b, $p, $n, $a, $t)\nON CONFLICT(tag_id) DO UPDATE SET\n    model = excluded.model, dim = excluded.dim, weights = excluded.weights,\n    bias = excluded.bias, positives = excluded.positives,\n    negatives = excluded.negatives, accuracy = excluded.accuracy,\n    trained_at = excluded.trained_at;";
		sqliteCommand.Parameters.AddWithValue("$tag", tagId);
		sqliteCommand.Parameters.AddWithValue("$model", "clip-vit-b32-vision");
		sqliteCommand.Parameters.AddWithValue("$dim", weights.Length);
		sqliteCommand.Parameters.AddWithValue("$w", MemoryMarshal.AsBytes(weights.AsSpan()).ToArray());
		sqliteCommand.Parameters.AddWithValue("$b", bias);
		sqliteCommand.Parameters.AddWithValue("$p", positives);
		sqliteCommand.Parameters.AddWithValue("$n", negatives);
		sqliteCommand.Parameters.AddWithValue("$a", accuracy);
		sqliteCommand.Parameters.AddWithValue("$t", Database.ToDb(DateTime.Now));
		sqliteCommand.ExecuteNonQuery();
	}

	private static (float[] Weights, float Bias)? LoadProbe(long tagId)
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT weights, bias FROM tag_probes WHERE tag_id = $t AND model = $m;";
		sqliteCommand.Parameters.AddWithValue("$t", tagId);
		sqliteCommand.Parameters.AddWithValue("$m", "clip-vit-b32-vision");
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		if (!sqliteDataReader.Read())
		{
			return null;
		}
		return (EmbeddingRepository.FromBlob((byte[])sqliteDataReader["weights"]), (float)sqliteDataReader.GetDouble(1));
	}
}
