using System.CommandLine;
using System.CommandLine.Binding;
using System.Net;

namespace CHttp.Binders;

internal class VersionBinder : Binder<Version>
{
    private readonly Option<string> _option;

    internal const string Version10 = "1.0";
    internal const string Version11 = "1.1";
    internal const string Version20 = "2";
    internal const string Version30 = "3";

    public VersionBinder(Option<string> option)
    {
        _option = option;
    }

    protected override Version GetBoundValue(BindingContext bindingContext)
	{
		var value = bindingContext.ParseResult.GetValueForOption(_option) ?? string.Empty;
		return Map(value);
	}

	internal static Version Map(string value)
	{
		return value switch
		{
			Version10 => HttpVersion.Version10,
			Version11 => HttpVersion.Version11,
			Version20 => HttpVersion.Version20,
			Version30 => HttpVersion.Version30,
			_ => throw new ArgumentException("Invalid version")
		};
	}
}