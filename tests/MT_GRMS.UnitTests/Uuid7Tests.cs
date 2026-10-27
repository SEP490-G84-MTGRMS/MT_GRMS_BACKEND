using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using Xunit;

namespace MT_GRMS.UnitTests;

public class Uuid7Tests
{
    [Fact]
    public void Uuid7_NewGuid_Should_Have_Version_7_And_RFC4122_Variant()
    {
        // GIVEN
        var guid = Uuid7.NewGuid();
        var bytes = guid.ToByteArray(bigEndian: true);

        // THEN: 4-bit version in byte 6 must be 7 (0b0111_xxxx)
        int version = (bytes[6] >> 4) & 0x0F;
        Assert.Equal(7, version);

        // THEN: 2-bit variant in byte 8 must be 2 (0b10xx_xxxx)
        int variant = (bytes[8] >> 6) & 0x03;
        Assert.Equal(2, variant);
    }

    [Fact]
    public async Task Uuid7_NewGuid_Should_Be_Monotonically_Increasing_Over_Time()
    {
        // GIVEN
        var id1 = Uuid7.NewGuid();
        await Task.Delay(10); // Đảm bảo bước nhảy timestamp mili-giây
        var id2 = Uuid7.NewGuid();

        // THEN: String comparison phải theo thứ tự thời gian tăng dần
        Assert.True(string.Compare(id1.ToString(), id2.ToString(), StringComparison.Ordinal) < 0,
            $"Expected {id1} to be lexicographically smaller than {id2}");
    }

    [Fact]
    public void BaseEntity_Should_Generate_Uuid7_ByDefault()
    {
        // GIVEN
        var product = new Product();
        var bytes = product.Id.ToByteArray(bigEndian: true);

        // THEN
        int version = (bytes[6] >> 4) & 0x0F;
        Assert.Equal(7, version);
    }
}
