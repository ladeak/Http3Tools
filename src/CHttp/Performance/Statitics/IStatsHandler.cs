using CHttp.Data;
using CHttp.Performance.Data;

namespace CHttp.Performance.Statitics;

internal interface IStatsHandler
{
    /// <summary>
    /// Handles a performance measurement session with the corresponding stats.
    /// </summary>
    ValueTask HandleStats(PerformanceMeasurementResults session, Stats stats);
}
