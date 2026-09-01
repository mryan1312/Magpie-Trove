using System.Collections.Generic;
using System.Linq;

namespace MagpieTrove.Services;

public sealed record DuplicateScan(List<DuplicateGroup> Groups, int Hashed, int Unreadable)
{
	public int TotalImages => Groups.Sum((DuplicateGroup g) => g.Count);

	public long ReclaimableBytes => Groups.Sum((DuplicateGroup g) => g.ReclaimableBytes);
}
