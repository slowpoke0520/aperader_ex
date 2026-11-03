using ApeRadar.History;
using System.Text.Json;
using Xunit;

namespace ApeRadar.Tests;

public sealed class ReplayBattleResultsDecoderTests
{
    [Theory]
    [InlineData("15.8.0", 11, 11)]
    [InlineData("15.9.0", 30, 30)]
    public void SanitizedRecordedSamples_PreserveCalibratedResults(string version, int expectedCount, int expectedKnownReceived)
    {
        using Stream stream = typeof(ReplayBattleResultsDecoderTests).Assembly.GetManifestResourceStream(
            "ApeRadar.Tests.Fixtures.ReplayBattleResultsSamples.json")!;
        Assert.NotNull(stream);
        using JsonDocument document = JsonDocument.Parse(stream);
        int count = 0, knownReceived = 0;
        foreach (JsonElement sample in document.RootElement.GetProperty("samples").EnumerateArray())
        {
            if (sample.GetProperty("version").GetString() != version) continue;
            count++;
            Assert.True(ReplayBattleResultsDecoder.TryDecode(sample.GetProperty("root"), Version.Parse(version),
                "Self", "101", out var result, out string error), $"{sample.GetProperty("id").GetString()}: {error}");
            JsonElement expected = sample.GetProperty("expected");
            Assert.Equal(expected.GetProperty("damage").GetInt64(), result!.Damage);
            Assert.Equal(expected.GetProperty("frags").GetDouble(), result.Frags);
            Assert.Equal(expected.GetProperty("result").GetString(), result.Result.ToString());
            Assert.Equal(expected.GetProperty("spotting").GetInt64(), result.SpottingDamage);
            Assert.Equal(expected.GetProperty("duration").GetDouble(), result.BattleDurationSeconds);
            Assert.Equal(expected.GetProperty("survived").GetBoolean(), result.Survived);
            Assert.Equal(expected.GetProperty("survival").GetDouble(), result.SurvivalSeconds);
            Assert.Equal(expected.GetProperty("receivedError").GetString(), result.DamageTakenError);
            if (expected.GetProperty("received").ValueKind == JsonValueKind.Null)
                Assert.Null(result.DamageTaken);
            else
            {
                knownReceived++;
                Assert.Equal(expected.GetProperty("received").GetInt64(), result.DamageTaken);
            }
        }
        Assert.Equal(expectedCount, count);
        Assert.Equal(expectedKnownReceived, knownReceived);
    }

    [Theory]
    [InlineData(0, 0, (int)BattleResult.Win)]
    [InlineData(0, 1, (int)BattleResult.Loss)]
    [InlineData(1, 1, (int)BattleResult.Win)]
    [InlineData(1, 255, (int)BattleResult.Draw)]
    public void CompleteResult_UsesVerifiedOwnRecordAndWinner(int team, int winner, int expected)
    {
        JsonElement root = ReplayResultsFixture.Create((_, own, common) => { own[6] = team; common[3] = winner; });
        Assert.True(Decode(root, out var result, out string error));
        Assert.Empty(error);
        Assert.Equal(120_222, result!.Damage);
        Assert.Equal(2, result.Frags);
        Assert.Equal((BattleResult)expected, result.Result);
        Assert.Equal(7_515, result.SpottingDamage);
        Assert.Equal(0, result.DamageTaken);
        Assert.Null(result.DamageTakenError);
        Assert.Equal(802, result.BattleDurationSeconds);
        Assert.True(result.Survived);
        Assert.Equal(802, result.SurvivalSeconds);
        Assert.Equal("42", result.AccountId);
        Assert.Equal("1234", result.ArenaId);
    }

    [Fact]
    public void AircraftParentAndSubtypeTotals_AreNotAddedTwice()
    {
        // The observed 15.8.0 overlap pattern is 34,271 if summed naively,
        // but 25,097 when the bomb and rocket family totals are counted once.
        JsonElement root = ReplayResultsFixture.Create((_, own, _) =>
        {
            own[201] = 1_881; own[202] = 13_384; own[216] = 495; own[220] = 163;
            own[205] = 5_610; own[208] = 5_610;
            own[228] = 3_564; own[230] = 3_564;
        });
        Assert.True(Decode(root, out var result, out _));
        Assert.Equal(25_097, result!.DamageTaken);
    }

    [Theory]
    [InlineData(205)]
    [InlineData(209)]
    [InlineData(228)]
    [InlineData(232)]
    public void AircraftFamilies_KeepAggregateOnce(int parent)
    {
        JsonElement root = ReplayResultsFixture.Create((_, own, _) =>
        {
            own[parent] = 1_000; own[parent + 1] = 300; own[parent + 2] = 700;
        });
        Assert.True(Decode(root, out var result, out _));
        Assert.Equal(1_000, result!.DamageTaken);
    }

    [Theory]
    [InlineData(214)] // No nonzero local AP secondary sample.
    [InlineData(213)] // No nonzero local ram sample.
    [InlineData(222)] // Event-specific damage.
    [InlineData(239)] // Building damage is outside the calibrated source.
    public void UncalibratedReceivedCategory_DoesNotProduceAPartialTotal(int index)
    {
        JsonElement root = ReplayResultsFixture.Create((_, own, _) => { own[199] = 10_000; own[index] = 1; });
        Assert.True(Decode(root, out var result, out _));
        Assert.Equal(120_222, result!.Damage);
        Assert.Null(result.DamageTaken);
        Assert.Equal("BattleResultsReceivedDamageCategoryUncalibrated", result.DamageTakenError);
    }

    [Theory]
    [InlineData(203)]
    [InlineData(204)]
    [InlineData(219)]
    public void CalibratedTorpedoAndManualSecondaryDamage_IsAddedIndependently(int index)
    {
        JsonElement root = ReplayResultsFixture.Create((_, own, _) => { own[199] = 10_000; own[index] = 4_333; });
        Assert.True(Decode(root, out var result, out _));
        Assert.Equal(14_333, result!.DamageTaken);
        Assert.Null(result.DamageTakenError);
    }

    [Fact]
    public void AntiSubmarineAirstrikeAliases_AreCountedOnce()
    {
        JsonElement root = ReplayResultsFixture.Create((_, own, _) =>
        {
            own[201] = 4_000; own[236] = 17_325; own[237] = 17_325;
        });
        Assert.True(Decode(root, out var result, out _));
        Assert.Equal(21_325, result!.DamageTaken);
        Assert.Null(result.DamageTakenError);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(100, 101)]
    public void AntiSubmarineAliasMismatch_KeepsTakenUnknown(long airSupport, long airBomb)
    {
        JsonElement root = ReplayResultsFixture.Create((_, own, _) => { own[236] = airSupport; own[237] = airBomb; });
        Assert.True(Decode(root, out var result, out _));
        Assert.Equal(120_222, result!.Damage);
        Assert.Null(result.DamageTaken);
        Assert.Equal("BattleResultsReceivedDamageAliasMismatch", result.DamageTakenError);
    }

    [Theory]
    [InlineData("negative")]
    [InlineData("fraction")]
    [InlineData("boolean")]
    [InlineData("missing")]
    [InlineData("family-exceeds-parent")]
    [InlineData("sum-overflow")]
    public void InvalidReceivedDamage_DoesNotDiscardValidCoreResult(string kind)
    {
        JsonElement root = ReplayResultsFixture.Create((_, own, _) =>
        {
            switch (kind)
            {
                case "negative": own[199] = -1; break;
                case "fraction": own[199] = 10.5; break;
                case "boolean": own[199] = true; break;
                case "missing": own[199] = null; break;
                case "family-exceeds-parent": own[205] = 100; own[206] = 101; break;
                case "sum-overflow": own[199] = long.MaxValue; own[200] = 1; break;
            }
        });
        Assert.True(Decode(root, out var result, out _));
        Assert.Equal(120_222, result!.Damage);
        Assert.Equal(2, result.Frags);
        Assert.Null(result.DamageTaken);
        Assert.NotEmpty(result.DamageTakenError!);
    }

    [Theory]
    [InlineData("15.7.0")]
    [InlineData("15.10.0")]
    [InlineData("15.8.1")]
    [InlineData("15.8.0.1")]
    public void UnverifiedVersion_IsNotDecoded(string version)
    {
        Assert.False(ReplayBattleResultsDecoder.TryDecode(ReplayResultsFixture.Create(), Version.Parse(version), "Self", "101", out var result, out string error));
        Assert.Null(result);
        Assert.Equal("BattleResultsVersionUnsupported", error);
    }

    [Theory]
    [InlineData("wrong-name")]
    [InlineData("wrong-ship")]
    [InlineData("wrong-row-account")]
    [InlineData("missing-owner")]
    [InlineData("duplicate-name")]
    public void IdentityMismatch_DoesNotUseAnotherPlayersScore(string kind)
    {
        JsonElement root = ReplayResultsFixture.Create((data, own, _) =>
        {
            var players = (Dictionary<string, object?>)data["playersPublicInfo"]!;
            switch (kind)
            {
                case "wrong-name": own[1] = "Other"; break;
                case "wrong-ship": own[7] = 102; break;
                case "wrong-row-account": own[0] = 43; break;
                case "missing-owner": data["accountDBID"] = 43; break;
                case "duplicate-name":
                    object?[] duplicate = (object?[])own.Clone(); duplicate[0] = 43;
                    players["43"] = duplicate;
                    break;
            }
        });
        Assert.False(Decode(root, out var result, out string error));
        Assert.Null(result);
        Assert.Equal("BattleResultsIdentityMismatch", error);
    }

    [Theory]
    [InlineData("short-row")]
    [InlineData("shifted-row")]
    [InlineData("common-length")]
    [InlineData("arena-mismatch")]
    [InlineData("different-mode")]
    public void WrongShape_IsRejectedEvenWhenSomeNumbersLookPlausible(string kind)
    {
        JsonElement root = ReplayResultsFixture.Create((data, own, common) =>
        {
            switch (kind)
            {
                case "short-row": ((Dictionary<string, object?>)data["playersPublicInfo"]!)["42"] = own[..537]; break;
                case "shifted-row": own[405] = 0; own[406] = new Dictionary<string, object?>(); break;
                case "common-length": data["commonList"] = common[..17]; break;
                case "arena-mismatch": common[0] = 5678; break;
                case "different-mode": common[12] = 8; break;
            }
        });
        Assert.False(Decode(root, out var result, out _));
        Assert.Null(result);
    }

    [Theory]
    [InlineData("damage-negative")]
    [InlineData("damage-fraction")]
    [InlineData("kills-negative")]
    [InlineData("kills-fraction")]
    [InlineData("kills-impossible")]
    [InlineData("winner-unknown")]
    [InlineData("team-unknown")]
    public void InvalidCoreMetric_CannotBecomeReplayExact(string kind)
    {
        JsonElement root = ReplayResultsFixture.Create((_, own, common) =>
        {
            switch (kind)
            {
                case "damage-negative": own[426] = -1; break;
                case "damage-fraction": own[426] = 100.5; break;
                case "kills-negative": own[32] = -1; break;
                case "kills-fraction": own[32] = 1.5; break;
                case "kills-impossible": own[32] = 99; break;
                case "winner-unknown": common[3] = 2; break;
                case "team-unknown": own[6] = 255; break;
            }
        });
        Assert.False(Decode(root, out _, out string error));
        Assert.Equal("BattleResultsMetricsInvalid", error);
    }

    [Fact]
    public void OptionalMetrics_DoNotTurnMissingIntoZero()
    {
        JsonElement root = ReplayResultsFixture.Create((_, own, common) =>
        {
            own[412] = null; own[21] = null; own[22] = null; common[8] = null;
        });
        Assert.True(Decode(root, out var result, out _));
        Assert.Null(result!.SpottingDamage);
        Assert.Null(result.Survived);
        Assert.Null(result.SurvivalSeconds);
        Assert.Null(result.BattleDurationSeconds);
    }

    [Fact]
    public void KnownZeroOutputAndReceivedDamage_ArePreserved()
    {
        JsonElement root = ReplayResultsFixture.Create((_, own, _) => { own[426] = 0; own[32] = 0; own[412] = 0; });
        Assert.True(Decode(root, out var result, out _));
        Assert.Equal(0, result!.Damage);
        Assert.Equal(0, result.Frags);
        Assert.Equal(0, result.SpottingDamage);
        Assert.Equal(0, result.DamageTaken);
    }

    [Fact]
    public void DuplicateEnvelopeKeys_AreRejected()
    {
        string json = ReplayResultsFixture.Create().GetRawText().Replace("\"accountDBID\":42", "\"accountDBID\":42,\"accountDBID\":42", StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.False(Decode(document.RootElement, out _, out _));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"damage\":12345,\"frags\":2,\"result\":\"win\"}")]
    public void MissingOrUnstructuredResults_AreRejected(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.False(Decode(document.RootElement, out var result, out string error));
        Assert.Null(result);
        Assert.Equal("BattleResultsSchemaInvalid", error);
    }

    private static bool Decode(JsonElement root, out ReplayBattleResults? result, out string error) =>
        ReplayBattleResultsDecoder.TryDecode(root, new Version(15, 8, 0), "Self", "101", out result, out error);
}

internal static class ReplayResultsFixture
{
    internal static JsonElement Create(Action<Dictionary<string, object?>, object?[], object?[]>? mutate = null)
    {
        object?[] own = Enumerable.Repeat<object?>(0, 538).ToArray();
        own[0] = 42; own[1] = "Self"; own[6] = 0; own[7] = 101;
        own[10] = Array.Empty<object>(); own[15] = 65_000; own[19] = null;
        own[20] = 53_230; own[21] = true; own[22] = 802; own[32] = 2;
        own[405] = new Dictionary<string, object?>(); own[406] = new Dictionary<string, object?>(); own[407] = false;
        own[412] = 7_515; own[426] = 120_222;
        object?[] common = Enumerable.Repeat<object?>(0, 18).ToArray();
        common[0] = 1234; common[3] = 0; common[8] = 802; common[12] = 7;
        common[14] = new Dictionary<string, object?>();
        Dictionary<string, object?> root = new()
        {
            ["accountDBID"] = 42, ["arenaUniqueID"] = 1234,
            ["playersPublicInfo"] = new Dictionary<string, object?> { ["42"] = own },
            ["commonList"] = common
        };
        mutate?.Invoke(root, own, common);
        return JsonSerializer.SerializeToElement(root);
    }
}
