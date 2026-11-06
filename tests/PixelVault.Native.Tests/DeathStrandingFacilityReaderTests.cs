using System.Buffers.Binary;
using System.Text.Json;
using PixelVaultNative;
using Xunit;

public class DeathStrandingFacilityReaderTests
{
    static readonly byte[] Header = Convert.FromHexString("2AB6AFE28110AC4C2395D359");
    static readonly byte[] Trailer = Convert.FromHexString("FF0000000000000000000000FF0000000000000000000000");

    [Fact]
    public void ReadsSavedLevelInsteadOfFriendshipScore()
    {
        var payload = Payload();
        var snapshot = DeathStrandingFacilityReader.Read(payload, "account", 1, "copy", "verified");
        Assert.Equal(36, snapshot.Items.Count);
        Assert.Equal(4, snapshot.Items.Single(i => i.ItemId == "050bcb50-57c0-f642-8aef-04b9e2c3cd13").Current);
        Assert.Equal(35, snapshot.Items.Count(i => i.Current == 5));
    }

    [Theory]
    [InlineData("wrong-household")]
    [InlineData("truncated")]
    [InlineData("duplicate-header")]
    public void RejectsIncompleteOrAmbiguousHouseholdBlocks(string corruption)
    {
        var payload = Payload();
        switch (corruption)
        {
            case "wrong-household": payload[Header.Length + 1] ^= 1; break;
            case "truncated": payload = payload[..^1]; break;
            case "duplicate-header": payload = payload.Concat(Header).ToArray(); break;
        }
        Assert.Throws<NotSupportedException>(() => DeathStrandingFacilityReader.Read(payload, "account", 1, "copy", "verified"));
    }

    static byte[] Payload()
    {
        using var catalogue = typeof(DeathStrandingFacilityReader).Assembly
            .GetManifestResourceStream("PixelVault.Tracking.DeathStrandingFacilities.json");
        var households = JsonSerializer.Deserialize<DeathStrandingFacilityReader.Household[]>(catalogue!);
        using var payload = new MemoryStream();
        payload.Write(Header);
        foreach (var household in households!)
        {
            var record = new byte[18];
            record[0] = 0x80;
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(1), household.HouseholdNameHash);
            record[5] = 0x81;
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), 2000);
            record[8] = (byte)(household.HouseholdNameHash == 1281133012 ? 4 : 5);
            record[9] = 1;
            payload.Write(record);
        }
        payload.Write(Trailer);
        return payload.ToArray();
    }
}
