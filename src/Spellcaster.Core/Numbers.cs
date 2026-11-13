using System;

namespace Spellcaster.Core;

public static class Numbers
{
    public static void RequireFiniteNonnegative(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }
}
