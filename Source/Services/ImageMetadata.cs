using System;
using System.Collections.Generic;

namespace MagpieTrove.Services;

public sealed record ImageMetadata(int Width, int Height, DateTime? DateTaken, int OrientationDegrees, string? CameraMake, string? CameraModel, string? Lens, int? Iso, double? Aperture, double? ShutterSpeed, double? FocalLength, IReadOnlyList<string> Keywords);
