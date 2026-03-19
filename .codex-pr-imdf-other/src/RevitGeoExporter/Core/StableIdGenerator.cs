using System;
using System.Security.Cryptography;
using System.Text;

namespace RevitGeoExporter.Core;

internal static class StableIdGenerator
{
    public static string Create(string scope, long elementId, string levelId)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("Scope is required.", nameof(scope));
        }

        if (string.IsNullOrWhiteSpace(levelId))
        {
            throw new ArgumentException("Level id is required.", nameof(levelId));
        }

        string seed = $"{scope.Trim()}|{elementId}|{levelId.Trim()}";
        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash).ToString();
    }
}
