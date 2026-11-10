using System.Globalization;
using System.Text;

namespace Garbati.ScheduleApi.Services.Implementations;

// Same keyset-paging token shape as Garbati.IdentityApi's JointApprovalService: opaque to the caller,
// base64 of the last row's internal Id. A pageToken that doesn't decode to this shape is a client input
// error (400), not "page not found."
public static class PageToken
{
    public static string Encode(long id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(id.ToString(CultureInfo.InvariantCulture)));

    public static bool TryDecode(string? pageToken, out long id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(pageToken))
        {
            return false;
        }

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(pageToken));
            return long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out id);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
