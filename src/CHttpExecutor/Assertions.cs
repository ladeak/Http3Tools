using CHttpExecutor;

namespace CHttpExecutor;

internal enum ComparingOperation
{
    LessThen,
    LessThenOrEquals,
    MoreThen,
    Equals,
    MoreThenOrEquals,
    NotEquals,
}

internal record struct Assertion(string ParameterName, double ComparisonValue, ComparingOperation Comperator);

internal record struct AssertionViolation(FrozenExecutionStep Step, Assertion ViolatedAssertion, double Value);