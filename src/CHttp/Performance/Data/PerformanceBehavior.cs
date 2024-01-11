namespace CHttp.Performance.Data;

internal record PerformanceBehavior(int RequestCount, int ClientsCount, bool SharedSocketsHandler);
