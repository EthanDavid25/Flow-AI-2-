using System.Globalization;
using System.Text.RegularExpressions;
using FlowAI.Core.Models;

namespace FlowAI.Core.Parsing;

/// <summary>
/// Parses the Clicky [POINT:x,y:label:screenN] and [BOX:x,y,w,h:label:screenN] coordinate tags
/// embedded in AI responses, and cleans the text for speech synthesis and subtitle bubbles.
/// </summary>
public static class PointProtocolParser
{
    // Regex matching [POINT:x,y:label] or [POINT:x,y:label:screenN]
    private static readonly Regex PointRegex = new(
        @"\[POINT:\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*:\s*([^:\]]+)(?::screen(\d+))?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex matching [BOX:x,y,w,h:label] or [BOX:x,y,w,h:label:screenN]
    private static readonly Regex BoxRegex = new(
        @"\[BOX:\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*:\s*([^:\]]+)(?::screen(\d+))?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<DetectedTarget> ParseTargets(string rawText, out string cleanText, IReadOnlyList<ScreenInfo>? screens = null)
    {
        var targets = new List<DetectedTarget>();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            cleanText = string.Empty;
            return targets;
        }

        // 1. Parse BOX tags
        var boxMatches = BoxRegex.Matches(rawText);
        foreach (Match match in boxMatches)
        {
            if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double y) &&
                double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double w) &&
                double.TryParse(match.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
            {
                string label = match.Groups[5].Value.Trim();
                int screenIndex = 0;
                if (match.Groups[6].Success && int.TryParse(match.Groups[6].Value, out int parsedScreen))
                {
                    screenIndex = parsedScreen;
                }

                ScreenInfo? screen = FindScreen(screens, screenIndex);
                BoundingBox box;

                // Check for normalized 0..1 scale
                if (screen != null && x >= 0.0 && x <= 1.0 && y >= 0.0 && y <= 1.0 && w <= 1.0 && h <= 1.0)
                {
                    box = Geometry.CoordinateTransform.NormalizedToScreenBox(x, y, w, h, screen);
                }
                else
                {
                    box = new BoundingBox(x, y, w, h);
                }

                var center = box.Center;
                if (screen != null)
                {
                    center = Geometry.CoordinateTransform.ClampToScreen(center, screen);
                }

                var target = new DetectedTarget(center.X, center.Y, label, screenIndex, box)
                {
                    Source = DetectionSource.PointProtocol
                };
                targets.Add(target);
            }
        }

        // 2. Parse POINT tags
        var pointMatches = PointRegex.Matches(rawText);
        foreach (Match match in pointMatches)
        {
            if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
            {
                string label = match.Groups[3].Value.Trim();
                int screenIndex = 0;
                if (match.Groups[4].Success && int.TryParse(match.Groups[4].Value, out int parsedScreen))
                {
                    screenIndex = parsedScreen;
                }

                ScreenInfo? screen = FindScreen(screens, screenIndex);
                CoordinatePoint pt;

                // Check for normalized 0..1 scale
                if (screen != null && x >= 0.0 && x <= 1.0 && y >= 0.0 && y <= 1.0)
                {
                    pt = Geometry.CoordinateTransform.NormalizedToScreenPixels(x, y, screen);
                }
                else
                {
                    pt = new CoordinatePoint(x, y);
                }

                if (screen != null)
                {
                    pt = Geometry.CoordinateTransform.ClampToScreen(pt, screen);
                }

                var target = new DetectedTarget(pt.X, pt.Y, label, screenIndex)
                {
                    Source = DetectionSource.PointProtocol
                };
                targets.Add(target);
            }
        }

        // 3. Strip all tags to produce clean spoken/display text
        string stripped = BoxRegex.Replace(rawText, string.Empty);
        stripped = PointRegex.Replace(stripped, string.Empty);

        // Normalize extra whitespace
        cleanText = Regex.Replace(stripped, @"[ \t]{2,}", " ").Trim();

        return targets;
    }

    private static ScreenInfo? FindScreen(IReadOnlyList<ScreenInfo>? screens, int screenIndex)
    {
        if (screens == null || screens.Count == 0) return null;
        for (int i = 0; i < screens.Count; i++)
        {
            if (screens[i].ScreenIndex == screenIndex) return screens[i];
        }
        return screens[0];
    }
}
