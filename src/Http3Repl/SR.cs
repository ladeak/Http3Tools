using System.Globalization;

namespace System.Net.Http
{
    internal partial class SR
    {
        internal static string Format(string resourceFormat, params object[] args)
        {
            if (args != null)
            {
                return string.Format(CultureInfo.CurrentCulture, resourceFormat, args);
            }

            return resourceFormat;
        }
    }
}
