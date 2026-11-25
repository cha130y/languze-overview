using LanguZe.Api.Modules.Storage.Contracts;
using NetVips;

namespace LanguZe.Api.Tests.Storage;

/// <summary>#73: how busy each part of a photo is, for placing the question's arrow.</summary>
public sealed class EdgeMapTests
{
    [Fact]
    public void A_flat_photo_has_no_edges_and_a_step_has_them_only_where_it_steps()
    {
        // Dark on the left four columns, bright on the right four.
        var luma = Enumerable.Range(0, 8 * 4).Select(i => i % 8 < 4 ? 0f : 255f).ToArray();

        var edges = EdgeMap.FromLuma(8, 4, luma, 2);

        Assert.All(EdgeMap.FromLuma(8, 4, new float[32], 2).Values, value => Assert.Equal(0, value));
        Assert.Equal(0, edges.Values[(1 * 8) + 0]);
        Assert.True(edges.Values[(1 * 8) + 3] > 1);
        Assert.True(edges.Busyness(0.5, 0.5, 0.1) > edges.Busyness(0.06, 0.5, 0.05));
    }

    [Fact]
    public void A_photo_is_read_at_160_columns_keeping_its_shape()
    {
        using var image = (Image.Black(800, 400) + 128).Cast(Enums.BandFormat.Uchar);

        var edges = EdgeMap.Of(image.JpegsaveBuffer());

        Assert.Equal((160, 80, 2.0), (edges.Columns, edges.Rows, edges.AspectRatio));
        Assert.True(edges.Values.Max() < 0.05);
    }

    // The arrow is a help: a photo libvips cannot read keeps its boxes and its words.
    [Fact]
    public void A_photo_that_cannot_be_read_has_no_edges_rather_than_an_error() =>
        Assert.Null(EdgeMap.TryOf(new byte[] { 1, 2, 3 }));
}
