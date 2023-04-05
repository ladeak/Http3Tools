using System.Numerics;

namespace CHttp.Writers;

public record struct Ratio<T>(T Numerator, T Total) where T : IBinaryNumber<T>;
