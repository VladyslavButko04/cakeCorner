using System.Security.Cryptography;
using System.Text;

namespace CakeCornerWeb.Recommendations.Runtime;

public static class CommerceEventKey
{
    public static Guid For(string identity) => new(SHA256.HashData(Encoding.UTF8.GetBytes("CakeCorner:Commerce:" + identity)).AsSpan(0, 16));
}
