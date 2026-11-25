using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Storage.Contracts;
using LanguZe.Api.Modules.Vocabulary.Contracts;

namespace LanguZe.Api.Modules.Worlds.Shared;

/// <summary>Where an occurrence's arrow points, where it starts, and where the spot came from (#73).</summary>
internal sealed record Arrow(WordPoint Point, WordPoint Tail, PointSource Source);

/// <summary>
/// The arrow an Identify question points at its object (#73, ADR-0004 "Points on the object").
///
/// A box often holds other things too, such as a desk's box holding the laptop on it, so the
/// arrow names the one object the question asks about. Its spot is the model's point when it gave
/// one inside the box; otherwise the middle of a box that holds no other word's box, or the part
/// of a box the others leave free. When none of these is safe there is no arrow and the question
/// shows the box alone: an arrow on the wrong object misleads more than no arrow.
///
/// The arrow then starts where the photo is plain (<see cref="EdgeMap"/>), away from the other
/// words' boxes and off the asked object, because the question's number sits on its tail.
/// </summary>
internal static class ArrowPlacement
{
    /// <summary>Share of another word's box inside this one at or above which this box is taken to hold it.</summary>
    public const double HoldsShare = 0.8;

    /// <summary>The least share of a box the others must leave free for its free part to be pointed at.</summary>
    public const double MinFreeShare = 0.3;

    /// <summary>Radius of the number on the tail, as a share of the photo's width.</summary>
    private const double BadgeRadius = 0.045;

    /// <summary>Arrow lengths tried, as shares of the photo's width: short first only when nothing longer fits well.</summary>
    private static readonly double[] Lengths = [0.13, 0.18, 0.23, 0.28, 0.33];

    /// <summary>One arrow for each item, in order; null where there is none.</summary>
    public static IReadOnlyList<Arrow?> Place(IReadOnlyList<CheckedItem> items, EdgeMap edges)
    {
        var arrows = new Arrow?[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var others = items.Where((_, at) => at != index).Select(item => item.Box).ToList();
            if (PointOf(items[index], others) is var (point, source))
            {
                arrows[index] = TailOf(point, items[index].Box, others, edges) is { } tail ? new Arrow(point, tail, source) : null;
            }
        }

        return arrows;
    }

    /// <summary>The spot the arrow points at, and where it came from; null when there is no safe one.</summary>
    public static (WordPoint Point, PointSource Source)? PointOf(CheckedItem item, IReadOnlyList<WordBox> others)
    {
        // The rules kept the model's point only inside its own box (ExtractionRules.CheckItem).
        if (item.Point is { } point)
        {
            return (point, PointSource.Model);
        }

        var box = item.Box;
        var held = others.Where(other => Inside(other, box) >= HoldsShare).ToList();
        if (held.Count == 0)
        {
            return (new WordPoint(box.X + (box.Width / 2), box.Y + (box.Height / 2)), PointSource.BoxCentre);
        }

        return FreeSpot(box, held) is { } free ? (free, PointSource.FreeSpot) : null;
    }

    /// <summary>
    /// The spot in the box farthest from every box it holds, on a grid, when the held boxes leave
    /// at least <see cref="MinFreeShare"/> of it free.
    /// </summary>
    private static WordPoint? FreeSpot(WordBox box, IReadOnlyList<WordBox> held)
    {
        const int Steps = 20;
        var free = new List<WordPoint>();
        for (var row = 0; row < Steps; row++)
        {
            for (var column = 0; column < Steps; column++)
            {
                var spot = new WordPoint(box.X + ((column + 0.5) * box.Width / Steps), box.Y + ((row + 0.5) * box.Height / Steps));
                if (!held.Any(other => Contains(other, spot, 0)))
                {
                    free.Add(spot);
                }
            }
        }

        if (free.Count < MinFreeShare * Steps * Steps)
        {
            return null;
        }

        return free.MaxBy(spot => held.Min(other => DistanceTo(other, spot)));
    }

    /// <summary>
    /// Where the arrow starts: every direction and length is tried, and the one whose number and
    /// shaft lie on the plainest part of the photo, clear of the other words and of the asked
    /// object, wins. Null when the photo leaves no room for the number at all.
    /// </summary>
    public static WordPoint? TailOf(WordPoint point, WordBox box, IReadOnlyList<WordBox> others, EdgeMap edges)
    {
        var aspect = edges.AspectRatio;
        var marginX = BadgeRadius * 1.3;
        var marginY = BadgeRadius * aspect * 1.3;
        WordPoint? best = null;
        var bestCost = double.MaxValue;
        for (var degrees = 0; degrees < 360; degrees += 10)
        {
            var radians = degrees * Math.PI / 180;
            foreach (var length in Lengths)
            {
                // The same length on screen whatever the direction: y is a share of the height.
                var (dx, dy) = (Math.Cos(radians) * length, Math.Sin(radians) * length * aspect);
                var tail = new WordPoint(point.X + dx, point.Y + dy);
                if (tail.X < marginX || tail.X > 1 - marginX || tail.Y < marginY || tail.Y > 1 - marginY)
                {
                    continue;
                }

                var cost = edges.Busyness(tail.X, tail.Y, BadgeRadius * 1.6) * 6;
                for (var t = 0.3; t <= 0.951; t += 0.05)
                {
                    cost += edges.Busyness(point.X + (dx * t), point.Y + (dy * t), 0.012);
                }

                foreach (var other in others)
                {
                    if (Contains(other, tail, 0.07))
                    {
                        cost += 8;
                    }

                    for (var t = 0.3; t <= 0.951; t += 0.1)
                    {
                        if (Contains(other, new WordPoint(point.X + (dx * t), point.Y + (dy * t)), 0))
                        {
                            cost += 0.6;
                        }
                    }
                }

                // The number is a disc, so its whole width keeps off the asked object.
                if (Contains(box, tail, BadgeRadius))
                {
                    cost += 5;
                }

                if (cost < bestCost)
                {
                    (best, bestCost) = (tail, cost);
                }
            }
        }

        return best;
    }

    private static bool Contains(WordBox box, WordPoint spot, double margin) =>
        spot.X >= box.X - margin && spot.X <= box.X + box.Width + margin
        && spot.Y >= box.Y - margin && spot.Y <= box.Y + box.Height + margin;

    private static double DistanceTo(WordBox box, WordPoint spot)
    {
        var dx = Math.Max(Math.Max(box.X - spot.X, 0), spot.X - (box.X + box.Width));
        var dy = Math.Max(Math.Max(box.Y - spot.Y, 0), spot.Y - (box.Y + box.Height));
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>The share of <paramref name="inner"/> that lies inside <paramref name="outer"/>.</summary>
    private static double Inside(WordBox inner, WordBox outer)
    {
        var width = Math.Min(inner.X + inner.Width, outer.X + outer.Width) - Math.Max(inner.X, outer.X);
        var height = Math.Min(inner.Y + inner.Height, outer.Y + outer.Height) - Math.Max(inner.Y, outer.Y);
        var area = inner.Width * inner.Height;
        return width > 0 && height > 0 && area > 0 ? width * height / area : 0;
    }
}
