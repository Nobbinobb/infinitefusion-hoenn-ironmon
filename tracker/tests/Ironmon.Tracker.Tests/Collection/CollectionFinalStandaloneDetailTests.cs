using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.Achievements;
using System.Security.Cryptography;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Checks the final approved standalone masters through the encrypted tracker detail reader.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _finalStandaloneDetailHashes = """
        {
          "e2d7c857-3565-5170-99f9-847714318e81": "6B610831558DFE62F9B0D700E262349BCEE2710158AE26482AE6814F9276FB64",
          "88379299-e293-517a-8c65-e2c6b7459998": "A6A915B318A379783D328BCDF76FE9A6149C246FADA9DBE9537B28CD762B390F",
          "5a00041f-6a7e-51ca-985e-21f5d7d9074f": "4DD31D299AF78DAFC5BEE1483DD9FD98C3E428695B1F8C7DA52F5427180D9794",
          "5a8b9bf2-47e1-508a-973f-7191567c307c": "E4E54039E60E3A4FCD30FFA1B656BA7C569F448A39E2C378FAD17A8567E6F19F",
          "15b7312f-a454-5742-9905-74aa3c11d9d6": "8E585D0E960BD00CE0E1738EAB750A57EBB080EFF2920CC440A2FD12422E44AE",
          "71790dc2-4cb0-5243-be1b-d7592adbc2e7": "1C342A09F87851CF277EC0FF358974406EA506A0A2B86F8518D0D7BDA3DE64C4",
          "f10fc049-3bd9-561a-831b-6572585805fe": "E0F98590E5DC54CBCC3A9A8EC49FF94EDBFF8547283AA0A07A92E4A0DFC40643",
          "8b00a02d-f462-5d60-b5f1-eb2cb814fb9f": "00D9BD73260F06818CA65D3E87B2DDDAF778F53692AF527DF10B2966FC5E5E55",
          "cb889a35-85f1-5c2a-b6ff-609ce09e2667": "9A8F47980F264227234575441DF677B8A7DB380A4835ED58779F08E520DB0029",
          "ac343977-aa6a-5116-aa19-5b1e6f818e7e": "49F9B40983791BAB8EBAAA8F0E758412383C64758F1E6D81BA7C7662AC83C77F",
          "2a1f14e0-d506-50f1-a957-4b561b93a49b": "4BD28FE45D6BBF3A13DE83B8B689405DD052D9BE6F61050075F42E50A2E56D27",
          "43cfae7f-84d6-5c0a-8585-0e76945aff7a": "F32ADDA37F9F0F3DFE48B68D0389582E022B0700102A6E98CDF2BDC5D4EDEA18",
          "47b74f7b-3a1e-54e4-a99b-54aa6a3b8e1a": "36D8DE9BFCFE6A918B13931130D619EE627A308BD2FBAA076E6FAEB3F6545B9F",
          "7044c79e-68d4-5ced-bb11-4790fbb1f56b": "2584835FEFA6B6CD6D4A6603CDCEDF568C6640EF91F6B5F7EC9B06920B0AEAFB",
          "17db4026-a6ef-5664-8783-1faf3aa19de3": "18B028BC66868FE3D9167E37C4B337E966B7D384EDAD9B22A0A1CA315CF3251D",
          "76008839-5c3d-5141-a837-35b10121abe5": "F568379944D69A7BE6E72056295BFFCCF08C68A93CB6FB88E45EAFFF14490AC0",
          "31d9d9c7-c5fd-58c2-a072-4eb98f82a4b0": "5806450BEFAB461B0DC9A2062FEE586ED2FE907DB9F9D67455E55461B230D41C",
          "ac815ec3-a2b0-5563-bc9c-88d00fa33579": "4A3548348B1B308C832389D0459F9559B1B1C92A8C4875B846938F9ABD22152B",
          "2a391c4d-131b-5009-90d5-07cec489f447": "607BD82FE2B2AEE58E7382559A76786BFE042CCE93E9DDD3E31A0E57B9EE609D",
          "4ef76baf-c2cc-5f89-9573-b337eadf748b": "CC877BFC536FB553C810F2BCC5BA39D6CDD060ACE3BB3A2A5F8C52E5581C8595",
          "62514a5c-e038-5b09-8d9d-9cb46002e6fe": "B5112D48852E7539748259BB0322F70CA052F05BADCC980E822F43F3098F4A64",
          "f7c51a97-382a-5067-b756-0e328315e661": "8620E46023B656020FED05040FFC02238523246477F644B3A58F515E9A70FA9A"
        }
        """;

    /// <summary>
    /// Authenticates all final standalone detail images while keeping locked artwork out of normal collection access.
    /// </summary>
    /// <returns>A task completing after exact detail snapshots, dimensions, and untouched progress are checked.</returns>
    [Fact]
    public async Task FinalStandaloneDetailArtworkMatchesApprovedMasters()
    {
        Dictionary<Guid, string> expected = JsonSerializer.Deserialize<Dictionary<Guid, string>>(_finalStandaloneDetailHashes)!;
        Assert.Equal(22, expected.Count);
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        foreach (var entry in expected)
        {
            Assert.Null(await store.GetAchievementArtworkAsync(entry.Key));
            Guid iconId = Assert.IsType<Guid>(AchievementIconPack.Shared.AssignedIconId(entry.Key));
            Assert.True(AchievementArtworkPack.Shared.Contains(iconId));
            AchievementArtwork detail = Assert.IsType<AchievementArtwork>(await AchievementArtworkPack.Shared.ForEarnedAsync(iconId, CancellationToken.None));
            Assert.Equal(1024, detail.Width);
            Assert.Equal(1024, detail.Height);
            byte[] png = Convert.FromBase64String(detail.ImageDataUri[_standalonePngPrefix.Length..]);
            Assert.Equal(entry.Value, Convert.ToHexString(SHA256.HashData(png)));
        }

        Assert.Empty(await store.GetAchievementsAsync());
        Assert.Empty(await store.GetAchievementsAsync(true));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, _assignedImagePattern, SearchOption.AllDirectories));
    }
}
