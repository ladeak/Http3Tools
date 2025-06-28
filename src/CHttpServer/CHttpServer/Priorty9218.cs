using Microsoft.Extensions.Primitives;

namespace CHttpServer;

public record struct Priority9218(uint Urgency, bool Incremental)
{
    private const uint DefaultUrgency = 3;
    private const bool DefaultIncremental = false;
    internal static Priority9218 Default { get; } = new Priority9218(DefaultUrgency, DefaultIncremental);

    internal static bool TryParse(StringValues values, out Priority9218 priority)
    {
        uint urgency = DefaultUrgency;
        bool incremental = DefaultIncremental;
        if (values.Count != 1)
        {
            priority = new Priority9218(urgency, incremental);
            return false;
        }
        var parameters = values[0].AsSpan();
        if (parameters.Length > 8)
        {
            priority = new Priority9218(urgency, incremental);
            return false;
        }

        foreach (var parameterRange in parameters.Split(','))
        {
            var parameter = parameters[parameterRange].Trim();
            if (parameter.Length > 2
                && parameter[0] == 'u'
                && parameter[1] == '='
                && parameter[2] >= '0'
                && parameter[2] <= '7')
                urgency = (uint)(parameter[2] - '0');
            if ((parameter.Length == 1 && parameter[0] == 'i')
                || (parameter.Length == 3 && parameter[0] == 'i' && parameter[1] == '=' && parameter[2] == '1'))
                incremental = true;
        }
        priority = new Priority9218(urgency, incremental);
        return true;
    }
}
