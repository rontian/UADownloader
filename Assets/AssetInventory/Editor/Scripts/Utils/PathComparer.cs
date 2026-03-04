using System;
using System.Collections.Generic;

namespace AssetInventory
{
    public class PathComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            // Handle null or identical cases up front if needed
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            // Split on '/'
            string[] xParts = x.Split('/');
            string[] yParts = y.Split('/');

            // Compare each part in sequence
            int minLength = Math.Min(xParts.Length, yParts.Length);
            for (int i = 0; i < minLength; i++)
            {
                // Compare ignoring case
                int result = string.Compare(xParts[i], yParts[i], StringComparison.OrdinalIgnoreCase);
                if (result != 0)
                {
                    return result;
                }
            }

            // If all common parts are equal, then the shorter path is considered "less"
            // (i.e., "3D" < "3D/Props" because it has fewer segments)
            return xParts.Length.CompareTo(yParts.Length);
        }
    }
}