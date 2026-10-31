using LanguZe.Api.Modules.Storage.Contracts;
using LanguZe.Api.Platform.Errors;
using NetVips;

namespace LanguZe.Api.Tests.Storage;

/// <summary>photo-preparation.service.spec.ts: preparing an uploaded photo (FR-017).</summary>
public sealed class PhotoPreparationTests
{
    private static readonly PhotoPreparation Preparation = new();

    [Fact]
    public void The_longer_side_is_scaled_down_to_2048_pixels()
    {
        var large = Photo(4000, 3000);

        var prepared = Preparation.Prepare(large).Prepared;

        Assert.Equal(PhotoPreparation.MaxPhotoSide, prepared.Width);
        Assert.Equal(1536, prepared.Height);
        Assert.True(prepared.ByteSize < large.Length);
        Assert.Equal(prepared.Data.Length, prepared.ByteSize);
    }

    // Scaling a small photo up would invent detail.
    [Fact]
    public void A_smaller_photo_keeps_its_own_size()
    {
        var prepared = Preparation.Prepare(Photo(800, 600)).Prepared;

        Assert.Equal(800, prepared.Width);
        Assert.Equal(600, prepared.Height);
    }

    [Fact]
    public void A_thumbnail_is_made_for_the_world_list()
    {
        var thumbnail = Preparation.Prepare(Photo(4000, 3000)).Thumbnail;

        Assert.Equal(PhotoPreparation.ThumbnailSide, thumbnail.Width);
        Assert.True(thumbnail.ByteSize < 50_000);
    }

    // A portrait photo carries its rotation as metadata; applying it to the pixels makes it the right way up everywhere.
    [Fact]
    public void The_camera_orientation_is_applied_to_the_pixels()
    {
        var prepared = Preparation.Prepare(Photo(1000, 500, orientation: 6)).Prepared;

        Assert.Equal(500, prepared.Width);
        Assert.Equal(1000, prepared.Height);
    }

    // The place a photo was taken must not survive the upload (FR-017, PDPA).
    [Fact]
    public void Every_piece_of_metadata_goes_including_where_it_was_taken()
    {
        var withExif = Photo(600, 400, exif: true);
        using (var source = Image.NewFromBuffer(withExif))
        {
            Assert.Contains("exif-ifd0-Copyright", source.GetFields());
            Assert.Contains(source.GetFields(), field => field.StartsWith("exif-ifd3-GPS", StringComparison.Ordinal));
        }

        var photo = Preparation.Prepare(withExif);

        foreach (var image in new[] { photo.Prepared, photo.Thumbnail })
        {
            using var stored = Image.NewFromBuffer(image.Data);
            Assert.DoesNotContain(stored.GetFields(), field => field.StartsWith("exif", StringComparison.Ordinal));
        }
    }

    // Everything is stored as JPEG, so one format is served and sent to AI.
    [Theory]
    [InlineData("png")]
    [InlineData("webp")]
    public void Png_and_webp_are_accepted_as_well(string format)
    {
        var prepared = Preparation.Prepare(Photo(100, 100, format: format)).Prepared;

        Assert.Equal("image/jpeg", prepared.ContentType);
        using var stored = Image.NewFromBuffer(prepared.Data);
        Assert.Equal("jpegload_buffer", stored.Get("vips-loader"));
    }

    // FR-011: the type is decided by the content, so a renamed file is still refused.
    [Fact]
    public void A_file_that_is_not_a_photo_is_refused()
    {
        var error = Assert.Throws<AppException>(() => Preparation.Prepare("This is a text file, not a photo."u8.ToArray()));

        Assert.Equal(ErrorCodes.PhotoTypeNotAllowed, error.Code);
        Assert.Equal(415, error.Status);
        Assert.Equal("This file could not be read as a photo.", error.Message);
    }

    [Fact]
    public void An_image_format_languze_does_not_accept_is_refused()
    {
        using var image = Plain(50, 50);

        var error = Assert.Throws<AppException>(() => Preparation.Prepare(image.TiffsaveBuffer()));

        Assert.Equal(ErrorCodes.PhotoTypeNotAllowed, error.Code);
        Assert.Equal("Only JPEG, PNG, and WebP photos can be used.", error.Message);
    }

    /// <summary>A photo of the given size, as a camera would hand it over.</summary>
    private static byte[] Photo(int width, int height, string format = "jpeg", int? orientation = null, bool exif = false)
    {
        using var plain = Plain(width, height);
        using var image = plain.Mutate(mutable =>
        {
            if (orientation is { } value)
            {
                mutable.Set(GValue.GIntType, "orientation", value);
            }

            if (exif)
            {
                mutable.Set(GValue.GStrType, "exif-ifd0-Copyright", "Nok");
                mutable.Set(GValue.GStrType, "exif-ifd3-GPSLatitude", "13/1 45/1 0/1");
                mutable.Set(GValue.GStrType, "exif-ifd3-GPSLongitude", "100/1 30/1 0/1");
            }
        });

        return format switch
        {
            "png" => image.PngsaveBuffer(),
            "webp" => image.WebpsaveBuffer(),
            _ => image.JpegsaveBuffer(keep: Enums.ForeignKeep.All),
        };
    }

    private static Image Plain(int width, int height)
    {
        using var black = Image.Black(width, height, bands: 3);
        return black.NewFromImage(180, 90, 40).Cast(Enums.BandFormat.Uchar);
    }
}
