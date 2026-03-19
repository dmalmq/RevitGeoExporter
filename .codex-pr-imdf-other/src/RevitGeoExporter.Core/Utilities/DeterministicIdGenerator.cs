using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RevitGeoExporter.Core.Utilities;

public static class DeterministicIdGenerator
{
    public static string CreateGuid(params string[] parts)
    {
        if (parts is null)
        {
            throw new ArgumentNullException(nameof(parts));
        }

        string[] normalized = parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim())
            .ToArray();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("At least one non-empty seed part is required.", nameof(parts));
        }

        string seed = string.Join("|", normalized);
        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash).ToString();
    }
}
