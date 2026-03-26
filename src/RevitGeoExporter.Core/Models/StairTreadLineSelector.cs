using System;
using System.Collections.Generic;

namespace RevitGeoExporter.Core.Models;

public readonly struct StairTreadLineCandidate
{
    public StairTreadLineCandidate(LineString2D line, double stationMeters)
    {
        Line = line ?? throw new ArgumentNullException(nameof(line));
        StationMeters = stationMeters;
    }

    public LineString2D Line { get; }

    public double StationMeters { get; }
}

public static class StairTreadLineSelector
{
    private const double MaxParallelDotProduct = 0.60d;

    public static bool TrySelectFrontEdge(
        IReadOnlyList<StairTreadLineCandidate> candidates,
        Point2D tangent,
        double minimumLengthMeters,
        out StairTreadLineCandidate selected)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        if (minimumLengthMeters <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLengthMeters), "Minimum length must be positive.");
        }

        selected = default;
        if (!TryNormalize(tangent.X, tangent.Y, out Point2D normalizedTangent))
        {
            return false;
        }

        bool found = false;
        double bestStation = 0d;
        double bestAlignment = 0d;
        double bestLength = 0d;
        for (int i = 0; i < candidates.Count; i++)
        {
            StairTreadLineCandidate candidate = candidates[i];
            double length = GetLength(candidate.Line.Points);
            if (length < minimumLengthMeters)
            {
                continue;
            }

            if (!TryNormalizeLine(candidate.Line, out Point2D direction))
            {
                continue;
            }

            double alignment = Math.Abs(Dot(direction, normalizedTangent));
            if (alignment > MaxParallelDotProduct)
            {
                continue;
            }

            if (!found ||
                candidate.StationMeters > bestStation + 1e-6d ||
                (Math.Abs(candidate.StationMeters - bestStation) <= 1e-6d &&
                 (alignment < bestAlignment - 1e-6d ||
                  (Math.Abs(alignment - bestAlignment) <= 1e-6d && length > bestLength + 1e-6d))))
            {
                selected = candidate;
                bestStation = candidate.StationMeters;
                bestAlignment = alignment;
                bestLength = length;
                found = true;
            }
        }

        return found;
    }

    public static IReadOnlyList<LineString2D> SelectDistinctOrderedLines(
        IReadOnlyList<StairTreadLineCandidate> candidates,
        double stationMergeThresholdMeters)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        if (stationMergeThresholdMeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(stationMergeThresholdMeters), "Merge threshold cannot be negative.");
        }

        if (candidates.Count == 0)
        {
            return Array.Empty<LineString2D>();
        }

        List<StairTreadLineCandidate> ordered = new(candidates);
        ordered.Sort((left, right) => left.StationMeters.CompareTo(right.StationMeters));

        List<StairTreadLineCandidate> selected = new();
        List<string> selectedKeys = new();
        HashSet<string> seenKeys = new(StringComparer.Ordinal);

        for (int i = 0; i < ordered.Count; i++)
        {
            StairTreadLineCandidate candidate = ordered[i];
            string key = BuildLineKey(candidate.Line);
            if (seenKeys.Contains(key))
            {
                continue;
            }

            if (selected.Count > 0 &&
                Math.Abs(candidate.StationMeters - selected[selected.Count - 1].StationMeters) <= stationMergeThresholdMeters)
            {
                StairTreadLineCandidate previous = selected[selected.Count - 1];
                if (GetLength(candidate.Line.Points) > GetLength(previous.Line.Points) + 1e-6d)
                {
                    seenKeys.Remove(selectedKeys[selectedKeys.Count - 1]);
                    selected[selected.Count - 1] = candidate;
                    selectedKeys[selectedKeys.Count - 1] = key;
                    seenKeys.Add(key);
                }

                continue;
            }

            selected.Add(candidate);
            selectedKeys.Add(key);
            seenKeys.Add(key);
        }

        List<LineString2D> lines = new(selected.Count);
        for (int i = 0; i < selected.Count; i++)
        {
            lines.Add(selected[i].Line);
        }

        return lines;
    }

    private static bool TryNormalizeLine(LineString2D line, out Point2D direction)
    {
        IReadOnlyList<Point2D> points = line.Points;
        Point2D start = points[0];
        Point2D end = points[points.Count - 1];
        return TryNormalize(end.X - start.X, end.Y - start.Y, out direction);
    }

    private static bool TryNormalize(double x, double y, out Point2D direction)
    {
        direction = default;
        double length = Math.Sqrt((x * x) + (y * y));
        if (length <= 1e-9d)
        {
            return false;
        }

        direction = new Point2D(x / length, y / length);
        return true;
    }

    private static double Dot(Point2D left, Point2D right)
    {
        return (left.X * right.X) + (left.Y * right.Y);
    }

    private static double GetLength(IReadOnlyList<Point2D> points)
    {
        double total = 0d;
        for (int i = 0; i < points.Count - 1; i++)
        {
            double dx = points[i + 1].X - points[i].X;
            double dy = points[i + 1].Y - points[i].Y;
            total += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return total;
    }

    private static string BuildLineKey(LineString2D line)
    {
        Point2D start = line.Points[0];
        Point2D end = line.Points[line.Points.Count - 1];
        return Compare(start, end) <= 0
            ? BuildLineKey(start, end)
            : BuildLineKey(end, start);
    }

    private static string BuildLineKey(Point2D start, Point2D end)
    {
        return $"{Math.Round(start.X, 4)}:{Math.Round(start.Y, 4)}:{Math.Round(end.X, 4)}:{Math.Round(end.Y, 4)}";
    }

    private static int Compare(Point2D left, Point2D right)
    {
        int xComparison = left.X.CompareTo(right.X);
        return xComparison != 0 ? xComparison : left.Y.CompareTo(right.Y);
    }
}
