using System.Reflection;
using System.Text.Json;
using Noxtend.Api.Contracts;
using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Application.Providers;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;
using Noxtend.Infrastructure.Persistence.InMemory;

namespace Noxtend.Tests.Application;

/// <summary>
/// Design Ref: §8.2 #22~24 — 공급자 키의 저장·유지·비노출.
///
/// **#24 가 Plan §4.1 의 "API 키가 어떤 응답에도 평문으로 나오지 않음 (테스트로 증명)"
/// 을 담당한다.** 이것이 인증 없는 사이클의 유일한 자동 방어선이다 (R-2).
/// </summary>
public sealed class ProviderHandlerTests
{
    private const string PlainKey = "sk-ant-secret-value-4f2c";

    private sealed class Fixture
    {
        public FixedClock Clock { get; } = FixedClock.Default;
        public InMemoryProviderConfigRepository Configs { get; } = new();
        public ReversibleProtector Protector { get; } = new();
        public TrackingModelCatalog Catalog { get; } = new();

        public CreateProviderHandler Create => new(Configs, Protector, Clock);
        public UpdateProviderHandler Update => new(Configs, Protector, Clock);
        public DeleteProviderHandler Delete => new(Configs);
        public ListProvidersHandler List => new(Configs);
        public TestProviderHandler Test => new(
            Configs,
            new ListProviderModelsHandler(Configs, Catalog));
    }

    private sealed class TrackingModelCatalog : IModelCatalog
    {
        private static readonly IReadOnlyList<ProviderModel> TextModels =
            [new("text-a", "Text A"), new("text-b", "Text B")];

        private static readonly IReadOnlyList<ProviderModel> ImageModels =
            [new("image-a", "Image A")];

        public int TextCalls { get; private set; }
        public int ImageCalls { get; private set; }

        public Task<IReadOnlyList<ProviderModel>> ListAsync(
            Guid providerConfigId,
            CancellationToken ct)
        {
            TextCalls++;
            return Task.FromResult(TextModels);
        }

        public Task<IReadOnlyList<ProviderModel>> ListImageModelsAsync(
            Guid providerConfigId,
            CancellationToken ct)
        {
            ImageCalls++;
            return Task.FromResult(ImageModels);
        }

        public int MeshCalls { get; private set; }

        private static readonly IReadOnlyList<ProviderModel> MeshModels =
            [new("P1-20260311", "Tripo P1")];

        public Task<IReadOnlyList<ProviderModel>> ListMeshModelsAsync(
            Guid providerConfigId,
            CancellationToken ct)
        {
            MeshCalls++;
            return Task.FromResult(MeshModels);
        }

        /// <summary>잔액을 읽었는지도 센다 — 연결 확인이 이것을 불러야 한다.</summary>
        public int BalanceCalls { get; private set; }

        public int? Balance { get; set; } = 2_400;

        public Task<int?> GetMeshCreditBalanceAsync(Guid providerConfigId, CancellationToken ct)
        {
            BalanceCalls++;
            return Task.FromResult(Balance);
        }
    }

    /// <summary>
    /// Data Protection 대역. 실제 구현은 키 링을 요구하므로 단위 테스트에 맞지 않는다.
    /// 검증 대상은 암호화 강도가 아니라 **평문이 어디로 흐르는가**다.
    /// </summary>
    private sealed class ReversibleProtector : ISecretProtector
    {
        private const string Prefix = "enc:";

        public string Protect(string plain) => Prefix + Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(plain));

        public string Unprotect(string cipher) => System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(cipher[Prefix.Length..]));
    }

    // #22 — 키 암호화 저장 + Last4 추출
    [Fact]
    public async Task Create_StoresCipherAndLast4_NeverThePlainKey()
    {
        var fixture = new Fixture();

        var result = await fixture.Create.HandleAsync(
            "Claude 운영", ProviderKind.Anthropic, PlainKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var config = result.Value!;

        Assert.NotEqual(PlainKey, config.ApiKeyCipher);
        Assert.DoesNotContain(PlainKey, config.ApiKeyCipher);
        Assert.Equal("4f2c", config.ApiKeyLast4);
        Assert.Equal("••••••••4f2c", config.MaskedApiKey);
        // 복호화하면 원래 키가 나온다 — 저장이 손실적이지 않다
        Assert.Equal(PlainKey, fixture.Protector.Unprotect(config.ApiKeyCipher));
    }

    [Fact]
    public async Task Create_RejectsEmptyKey()
    {
        var fixture = new Fixture();

        var result = await fixture.Create.HandleAsync(
            "이름", ProviderKind.OpenAI, "  ", CancellationToken.None);

        Assert.Equal(ErrorCode.ProviderKeyRequired, result.ErrorCode);
    }

    // #23 — apiKey=null → 기존 암호문 유지
    [Fact]
    public async Task Update_KeepsExistingCipher_WhenApiKeyIsOmitted()
    {
        var fixture = new Fixture();
        var created = (await fixture.Create.HandleAsync(
            "Claude 운영", ProviderKind.Anthropic, PlainKey, CancellationToken.None)).Value!;
        var originalCipher = created.ApiKeyCipher;

        var result = await fixture.Update.HandleAsync(
            created.Id, "Claude 운영 (수정)", ProviderKind.Anthropic,
            apiKey: null, isEnabled: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // 화면이 키를 되읽을 수 없으므로, 재입력을 요구하면 실수로 지워진다 (§4.2 #11)
        Assert.Equal(originalCipher, result.Value!.ApiKeyCipher);
        Assert.Equal("4f2c", result.Value.ApiKeyLast4);
        Assert.Equal("Claude 운영 (수정)", result.Value.DisplayName);
    }

    [Fact]
    public async Task Update_TreatsBlankKeyAsOmitted()
    {
        var fixture = new Fixture();
        var created = (await fixture.Create.HandleAsync(
            "이름", ProviderKind.OpenAI, PlainKey, CancellationToken.None)).Value!;
        var originalCipher = created.ApiKeyCipher;

        // 폼이 빈 입력창을 그대로 보내는 흔한 경우 — 사용자는 아무것도 안 했다
        var result = await fixture.Update.HandleAsync(
            created.Id, "이름", ProviderKind.OpenAI,
            apiKey: "   ", isEnabled: true, CancellationToken.None);

        Assert.Equal(originalCipher, result.Value!.ApiKeyCipher);
    }

    [Fact]
    public async Task Update_ReplacesCipher_WhenNewKeyIsProvided()
    {
        var fixture = new Fixture();
        var created = (await fixture.Create.HandleAsync(
            "이름", ProviderKind.OpenAI, PlainKey, CancellationToken.None)).Value!;

        var result = await fixture.Update.HandleAsync(
            created.Id, "이름", ProviderKind.OpenAI,
            apiKey: "sk-new-key-abcd", isEnabled: false, CancellationToken.None);

        Assert.Equal("abcd", result.Value!.ApiKeyLast4);
        Assert.False(result.Value.IsEnabled);
        Assert.Equal("sk-new-key-abcd", fixture.Protector.Unprotect(result.Value.ApiKeyCipher));
    }

    [Fact]
    public async Task Update_ReportsNotFoundForUnknownId()
    {
        var fixture = new Fixture();

        var result = await fixture.Update.HandleAsync(
            Guid.NewGuid(), "이름", ProviderKind.OpenAI,
            null, true, CancellationToken.None);

        Assert.Equal(ErrorCode.ProviderNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Delete_RemovesConfig()
    {
        var fixture = new Fixture();
        var created = (await fixture.Create.HandleAsync(
            "이름", ProviderKind.OpenAI, PlainKey, CancellationToken.None)).Value!;

        Assert.True((await fixture.Delete.HandleAsync(created.Id, CancellationToken.None)).IsSuccess);
        Assert.Empty(await fixture.List.HandleAsync(CancellationToken.None));
    }

    // #24 — 공급자 응답 DTO 의 어떤 필드에도 평문 키가 없다
    [Fact]
    public async Task ProviderResponse_ContainsNoPlainKeyAndNoCipher()
    {
        var fixture = new Fixture();
        var config = (await fixture.Create.HandleAsync(
            "Claude 운영", ProviderKind.Anthropic, PlainKey, CancellationToken.None)).Value!;

        var dto = ProviderResponse.From(config);
        var json = JsonSerializer.Serialize(dto);

        // 직렬화 결과 전체를 본다 — 필드를 하나씩 확인하면 새 필드가 추가될 때 놓친다.
        // 키는 ASCII 라 인코더가 무엇이든 문자열 검사로 잡힌다
        Assert.DoesNotContain(PlainKey, json);
        Assert.DoesNotContain(config.ApiKeyCipher, json);

        // 마스킹은 역직렬화해서 본다 — 기본 인코더가 •를 • 로 escape 하므로
        // 원시 문자열 비교는 인코더 설정에 따라 깨진다
        Assert.Equal("••••••••4f2c", JsonSerializer.Deserialize<ProviderResponse>(json)!.ApiKeyMasked);
    }

    [Theory]
    [InlineData(ProviderKind.OpenAI, "textAnalysis", "imageGeneration", "similarityEvaluation")]
    [InlineData(ProviderKind.Anthropic, "textAnalysis", "similarityEvaluation")]
    // Google 은 main 에서 텍스트 어댑터가 들어왔지만, 2-이미지 strict 평가는 미검증이라
    // similarityEvaluation 은 아직 열지 않는다
    [InlineData(ProviderKind.Google, "textAnalysis", "imageGeneration")]
    public async Task ProviderResponse_AdvertisesOnlyApplicationCapabilities(
        ProviderKind kind,
        params string[] expected)
    {
        var fixture = new Fixture();
        var config = (await fixture.Create.HandleAsync(
            $"{kind} 운영", kind, PlainKey, CancellationToken.None)).Value!;

        var json = JsonSerializer.SerializeToElement(ProviderResponse.From(config));
        var capabilities = json.GetProperty("Capabilities")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();

        Assert.Equal(expected, capabilities);
    }

    [Theory]
    [InlineData(ProviderKind.OpenAI, "openai")]
    [InlineData(ProviderKind.Anthropic, "anthropic")]
    [InlineData(ProviderKind.Google, "google")]
    public async Task ProviderResponse_UsesFrontendProviderKindContract(
        ProviderKind kind,
        string expected)
    {
        var fixture = new Fixture();
        var config = (await fixture.Create.HandleAsync(
            $"{kind} 운영", kind, PlainKey, CancellationToken.None)).Value!;

        Assert.Equal(expected, ProviderResponse.From(config).Kind);
    }

    [Theory]
    [InlineData(ProviderKind.OpenAI, 1, 1)]
    [InlineData(ProviderKind.Anthropic, 1, 0)]
    [InlineData(ProviderKind.Google, 1, 1)]
    public async Task TestProvider_ChecksOnlySupportedCapabilities(
        ProviderKind kind,
        int expectedTextCalls,
        int expectedImageCalls)
    {
        var fixture = new Fixture();
        var config = (await fixture.Create.HandleAsync(
            $"{kind} 운영", kind, PlainKey, CancellationToken.None)).Value!;

        var result = await fixture.Test.HandleAsync(config.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedTextCalls, fixture.Catalog.TextCalls);
        Assert.Equal(expectedImageCalls, fixture.Catalog.ImageCalls);
    }

    /// <summary>
    /// **3D 공급자만 잔액을 읽는다.** 텍스트·이미지 공급자에 잔액 조회를 보내면 그쪽
    /// endpoint 가 없어 헛된 호출이 된다.
    /// </summary>
    [Theory]
    [InlineData(ProviderKind.Tripo, 1)]
    [InlineData(ProviderKind.Meshy, 1)]
    [InlineData(ProviderKind.OpenAI, 0)]
    [InlineData(ProviderKind.Google, 0)]
    public async Task TestProvider_ReadsBalanceOnlyForMeshProviders(
        ProviderKind kind, int expectedBalanceCalls)
    {
        var fixture = new Fixture();
        var config = (await fixture.Create.HandleAsync(
            $"{kind} 운영", kind, PlainKey, CancellationToken.None)).Value!;

        var result = await fixture.Test.HandleAsync(config.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedBalanceCalls, fixture.Catalog.BalanceCalls);
        Assert.Equal(
            expectedBalanceCalls == 0 ? null : fixture.Catalog.Balance,
            result.Value!.MeshCreditBalance);
    }

    /// <summary>
    /// **잔액을 못 읽어도 연결 확인은 성공이다.** 키는 모델 목록 조회에서 이미 확인됐고,
    /// 잔액은 있으면 좋은 값이다 — 못 읽었다고 "연결 실패" 로 보이면 사용자가 키를
    /// 다시 넣는다.
    /// </summary>
    [Fact]
    public async Task TestProvider_SucceedsEvenWhenBalanceIsUnknown()
    {
        var fixture = new Fixture();
        fixture.Catalog.Balance = null;

        var config = (await fixture.Create.HandleAsync(
            "Meshy 운영", ProviderKind.Meshy, PlainKey, CancellationToken.None)).Value!;

        var result = await fixture.Test.HandleAsync(config.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // **0 이 아니라 null 이다** — 0 은 "다 썼다" 이고 null 은 "모른다" 다
        Assert.Null(result.Value!.MeshCreditBalance);
    }

    [Fact]
    public void ProviderTestResponse_ExposesCountsByCapability()
    {
        var actual = typeof(ProviderTestResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();

        // capability 마다 세는 값이 하나씩이다 — 3D 가 붙으며 셋이 됐다 (사이클 #10).
        // 잔액은 세는 값이 아니라 **남은 양**이라 따로 선다 — 3D 는 파츠 하나가 크레딧
        // 30 이라, 모르고 시작하면 작업 도중에 멈춘다
        string[] expected =
        [
            "ImageModelCount", "LatencyMs", "MeshCreditBalance",
            "MeshModelCount", "Ok", "TextModelCount",
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void KindCapabilities_CoverEveryKind_AndUseTheFrontendKindSpelling()
    {
        var table = Enum.GetValues<ProviderKind>()
            .Select(ProviderKindCapabilitiesResponse.From)
            .ToArray();

        // 관리자 폼이 이 표로 "이 종류는 무엇에 쓰이나" 를 보여준다 —
        // 종류가 늘고 여기 빠지면 폼이 빈칸을 그린다
        Assert.Equal(Enum.GetValues<ProviderKind>().Length, table.Length);

        // 머리글자 약어는 camelCase 가 아니라 소문자다 (`openAI` 가 아니라 `openai`)
        Assert.Contains(table, entry => entry.Kind == "openai");
        Assert.DoesNotContain(table, entry => entry.Kind.Any(char.IsUpper));

        var google = table.Single(entry => entry.Kind == "google");
        Assert.Equal(["textAnalysis", "imageGeneration"], google.Capabilities);
    }

    /// <summary>
    /// DTO 에 키를 나르는 속성이 추가되면 여기서 걸린다. 위 테스트는 이 키 값에 대해서만
    /// 참이지만, 이 테스트는 **모양**을 고정한다.
    /// </summary>
    [Fact]
    public void ProviderResponse_ExposesOnlyTheAgreedFields()
    {
        var actual = typeof(ProviderResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        // Model 이 빠졌다 — 공급자는 접속 수단이고 모델은 공정이 갖는다
        string[] expected =
            ["ApiKeyMasked", "Capabilities", "DisplayName", "Id", "IsEnabled", "Kind"];

        Assert.Equal(expected, actual);
    }
}
