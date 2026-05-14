using System;
using System.Text;

namespace OpenUtauMobile.Tools;

public static class GuidTools
{
    public static Guid CreateGuidFromStrings(string str1, string str2)
    {
        // Combine the two strings
        string combined = str1 + str2;
        // Compute a hash of the combined string
        byte[] hash = System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(combined));
        // Create a new GUID from the hash
        return new Guid(hash);
    }
}