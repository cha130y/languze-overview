using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Storage.Contracts;
using LanguZe.Api.Modules.Vocabulary.Contracts;
using LanguZe.Api.Modules.Worlds.Shared;

namespace LanguZe.Api.Tests.Worlds;

/// <summary>#73: where a question's arrow points, and where it starts.</summary>
public sealed class ArrowPlacementTests
{
    private static readonly WordBox Desk = new(0.1, 0.5, 0.8, 0.4);

    private static readonly WordBox LaptopOnDesk = new(0.3, 0.55, 0.3, 0.2);

    /// <summary>A plain photo, as wide as it is high.</summary>
    private static readonly EdgeMap Plain = EdgeMap.FromLuma(40, 40, new float[40 * 40], 1);

    [Fact]
    public void The_models_point_is_used_when_it_gave_one() =>
        Assert.Equal((new WordPoint(0.2, 0.85), PointSource.Model), ArrowPlacement.PointOf(Item(Desk, new WordPoint(0.2, 0.85)), [LaptopOnDesk]));

    [Fact]
    public void Without_a_point_a_box_holding_no_other_is_pointed_at_in_its_middle() =>
        Assert.Equal((new WordPoint(0.5, 0.7), PointSource.BoxCentre), ArrowPlacement.PointOf(Item(Desk), [new WordBox(0.1, 0.1, 0.2, 0.2)]));

    // The middle of the desk's box is the laptop: the arrow goes where the laptop leaves the desk free.
    [Fact]
    public void Without_a_point_a_box_holding_others_is_pointed_at_where_they_leave_it_free()
    {
        var (point, source) = ArrowPlacement.PointOf(Item(Desk), [LaptopOnDesk])!.Value;

        Assert.Equal(PointSource.FreeSpot, source);
        Assert.False(point.X >= 0.3 && point.X <= 0.6 && point.Y >= 0.55 && point.Y <= 0.75);
        Assert.True(point.X >= 0.1 && point.X <= 0.9 && point.Y >= 0.5 && point.Y <= 0.9);
    }

    // An arrow on the wrong object misleads more than no arrow.
    [Fact]
    public void Without_a_point_a_box_its_others_nearly_fill_gets_no_arrow() =>
        Assert.Null(ArrowPlacement.PointOf(Item(Desk), [new WordBox(0.1, 0.5, 0.7, 0.4)]));

    [Fact]
    public void The_arrow_starts_on_the_plain_part_of_the_photo()
    {
        // Stripes two pixels wide everywhere but the right third.
        var luma = new float[40 * 40];
        for (var i = 0; i < luma.Length; i++)
        {
            luma[i] = i % 40 < 27 && i % 40 / 2 % 2 == 0 ? 255 : 0;
        }

        var tail = ArrowPlacement.TailOf(new WordPoint(0.6, 0.5), new WordBox(0.55, 0.45, 0.1, 0.1), [], EdgeMap.FromLuma(40, 40, luma, 1))!;

        Assert.True(tail.X > 0.68, $"tail at {tail}");
    }

    [Fact]
    public void The_arrow_starts_clear_of_the_other_words_and_inside_the_photo()
    {
        WordBox[] others = [new(0, 0, 1, 0.45), new(0, 0.55, 0.45, 0.45)];

        var tail = ArrowPlacement.TailOf(new WordPoint(0.5, 0.5), new WordBox(0.45, 0.45, 0.1, 0.1), others, Plain)!;

        Assert.True(tail.X > 0.45 && tail.Y > 0.45, $"tail at {tail}");
        Assert.InRange(tail.X, 0, 1);
        Assert.InRange(tail.Y, 0, 1);
    }

    [Fact]
    public void Each_item_gets_its_arrow_or_none_in_order()
    {
        CheckedItem[] items = [Item(Desk), Item(LaptopOnDesk, new WordPoint(0.4, 0.6)), Item(new WordBox(0.1, 0.5, 0.8, 0.4))];

        var arrows = ArrowPlacement.Place(items, Plain);

        // The desk's box holds the laptop and a box the size of itself, which leaves it nothing free.
        Assert.Null(arrows[0]);
        Assert.Equal(PointSource.Model, arrows[1]!.Source);
        Assert.Null(arrows[2]);
    }

    private static CheckedItem Item(WordBox box, WordPoint? point = null) =>
        new("desk", "โต๊ะ", "The desk is big.", CefrLevel.A1, [], box, point);
}
