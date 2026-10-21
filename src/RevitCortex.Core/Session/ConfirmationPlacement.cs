using System;

namespace RevitCortex.Core.Session;

public static class ConfirmationPlacement
{
    public static (int X, int Y) Center(int left, int top, int width, int height, int windowWidth, int windowHeight) =>
        (left + Math.Max(0, (width - windowWidth) / 2), top + Math.Max(0, (height - windowHeight) / 2));
}
