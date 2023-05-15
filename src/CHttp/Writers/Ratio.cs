using System.Numerics;

namespace CHttp.Writers;

public record struct Ratio<T>(T Numerator, T Total, TimeSpan Remaining) where T : IBinaryNumber<T>;
