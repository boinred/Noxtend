using Noxtend.Application.Common;
using Noxtend.Application.Job;
using Noxtend.Application.Pipeline;
using Noxtend.Application.Stages;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Generation;

/// <summary>
/// 파츠 생성 단계 — 프롬프트 변수 조립과 응답 검증.
///
/// Design Ref: §2.3 A-1b · §9.2
///
/// **<c>IImageStage</c> 인터페이스를 두지 않는다** (원칙 ②). 이미지 단계가 하나뿐인데
/// 인터페이스와 레지스트리를 만들면 구현체 하나짜리 추상이 남고, 그 추상은 검증되지
/// 않는다. 조립 단계가 붙어 이미지 단계가 둘이 될 때 추상화하면 되고, 그때의 변경은
/// 추가형이다.
///
/// **Application 에 있는 이유** (§9.2, 사이클 #5 와 같다): Domain 에 두면 도메인이
/// 프롬프트 변수와 공급자 응답 형식을 알게 되고, Infrastructure 에 두면 어댑터가 단계를
/// 알게 되어 G-1 이 깨진다. **프롬프트 조립과 응답 검증은 도메인 규칙이 아니라 공급자와의
/// 대화 방식이다.**
/// </summary>
public sealed class GenerationStage(GenerationOptions options)
{
    public TaskKind Kind => TaskKind.Generate;

    /// <summary>
    /// 이 공정이 그리는 파츠의 값들.
    ///
    /// **파츠 하나만 들어간다** — 분해와 반대다. 분해는 가림 관계 때문에 파츠 목록 전체가
    /// 필요했지만, 생성은 파츠마다 독립이고 다른 파츠를 알려주면 그것들까지 그릴 유인이 된다
    /// (Plan R-2).
    /// </summary>
    public IReadOnlyDictionary<string, string> BuildVariables(
        PipelineJob job,
        AssetPart part,
        ViewDirection viewDirection)
        => new Dictionary<string, string>
        {
            ["scene"] = StageJson.Describe(ExtractStage.RequireScene(job)),

            // 성별은 베이스바디를 그리는 데 필수라 생성에도 넣는다. partHints 는 넣지 않는다 —
            // 파츠 하나만 그리는 단계에 다른 파츠 목록을 주면 그것들까지 그릴 유인이 된다 (§D-03 · R-2)
            ["gender"] = CharacterVariables.GenderText(job.Gender),
            ["partName"] = part.Name,

            // 분해가 채우지 못한 파츠는 이름만으로 그린다 — 여기서 실패시키면 앞 단계의
            // 부분적 성공이 뒤에서 전체 실패가 된다
            ["partDescription"] = part.Description ?? part.Name,
            ["partCategory"] = part.Category ?? "미분류",
            ["viewDirection"] = viewDirection switch
            {
                ViewDirection.Front => "front view (0 degrees)",
                ViewDirection.Right => "right view (90 degrees)",
                ViewDirection.Back => "back view (180 degrees)",
                ViewDirection.Left => "left view (270 degrees)",
                _ => throw new ArgumentOutOfRangeException(nameof(viewDirection)),
            },
        };

    /// <summary>
    /// 응답 검증 (FR-09).
    ///
    /// **품질 판단이 아니라 오류 판정이다** (Plan D-9 의 연장). "잘 그렸는가" 는 아직
    /// 사람 눈밖에 없고, 여기서 보는 것은 명백히 쓸 수 없는 응답뿐이다.
    ///
    /// 세 가지로 나뉜다:
    /// ① 바이트가 없다 → 다시 걸어본다 (모델이 한 번 흘린 것일 수 있다)
    /// ② 형식이 이미지가 아니다 → 다시 걸어본다
    /// ③ 크기 상한 초과 → **즉시 실패** (다시 걸어도 같은 모델이 같은 크기를 낸다)
    /// </summary>
    /// <exception cref="GenerationException">검증 실패</exception>
    public void Validate(ImageResult result)
    {
        if (result.Bytes.Length == 0)
        {
            throw new GenerationException(
                ErrorCode.GenerationEmptyResponse, FailureDisposition.Retry,
                "공급자가 이미지를 내지 않았습니다");
        }

        if (!options.AllowedContentTypes.Contains(result.ContentType))
        {
            throw new GenerationException(
                ErrorCode.GenerationUnsupportedFormat, FailureDisposition.Retry,
                $"지원하지 않는 형식입니다: {result.ContentType}");
        }

        if (result.Bytes.Length > options.MaxImageBytes)
        {
            throw new GenerationException(
                ErrorCode.GenerationImageTooLarge, FailureDisposition.Fail,
                $"이미지가 상한을 넘습니다: {result.Bytes.Length}바이트");
        }
    }
}

/// <summary>
/// 검증 실패 — 실패 코드와 재시도 여부를 함께 나른다.
///
/// **재시도 여부를 예외가 들고 다니는 이유**: 판정하는 곳(여기)과 반영하는 곳
/// (<c>TaskExecution</c>)이 다르므로, 핸들러가 코드를 보고 다시 판단하면 규칙이 두 곳에
/// 생긴다. 크기 초과만 즉시 실패라는 사실이 한 자리에 남아야 한다.
/// </summary>
public sealed class GenerationException(
    string code, FailureDisposition disposition, string message) : Exception(message)
{
    public string Code => code;

    public FailureDisposition Disposition => disposition;
}
