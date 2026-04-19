// Services/PromptHasher.cs
using System.Security.Cryptography;
using System.Text;

namespace TallgrassAgentApi.Services;

public static class PromptHasher
{
    public static string Hash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}