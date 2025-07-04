using Microsoft.Extensions.Primitives;

namespace CHttpServer;

public record struct Priority9218(byte Urgency, bool Incremental) : IComparable<Priority9218>
{
    private const byte DefaultUrgency = 3;
    private const bool DefaultIncremental = false;
    internal static Priority9218 Default { get; } = new Priority9218(DefaultUrgency, DefaultIncremental);

    internal static bool TryParse(StringValues values, out Priority9218 priority)
    {
        byte urgency = DefaultUrgency;
        bool incremental = DefaultIncremental;
        if (values.Count != 1)
        {
            priority = new Priority9218(urgency, incremental);
            return false;
        }
        var parameters = values[0].AsSpan();
        if (parameters.Length > 32)
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
                urgency = (byte)(parameter[2] - '0');
            if ((parameter.Length == 1 && parameter[0] == 'i')
                || (parameter.Length == 3 && parameter[0] == 'i' && parameter[1] == '=' && parameter[2] == '1'))
                incremental = true;
        }
        priority = new Priority9218(urgency, incremental);
        return true;
    }

    public int CompareTo(Priority9218 other)
    {
        if (Urgency == other.Urgency)
        {
            if (Incremental)
                return other.Incremental ? 0 : 1;
            return other.Incremental ? -1 : 0;
        }
        return (int)other.Urgency - (int)Urgency;
    }
}