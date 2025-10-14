using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Utils
{
    public static class GuidTools
    {
        public static Guid CreateGuidFromStrings(string str1, string str2)
        {
            // Combine the two strings
            string combined = str1 + str2;
            // Compute a hash of the combined string
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(combined));
                // Create a new GUID from the hash
                return new Guid(hash);
            }
        }
    }
}
