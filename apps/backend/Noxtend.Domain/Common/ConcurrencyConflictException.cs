namespace Noxtend.Domain.Common;

/// <summary>
/// 저장 시점에 다른 요청과 충돌했다 — RowVersion 낙관적 동시성 위반이거나, 필터
/// 유니크 제약(예: 파츠당 하나뿐인 Reconstruct 공정)을 동시에 두 요청이 건드렸을 때.
///
/// **Infrastructure 가 EF Core 예외(`DbUpdateConcurrencyException`/`DbUpdateException`)를
/// 여기로 번역해 던진다.** Application 계층은 EF Core 를 참조하지 않으므로, 그 계층이
/// 잡을 수 있는 형태가 Domain 에 있어야 한다(merge-gate 리뷰 F2).
/// </summary>
public sealed class ConcurrencyConflictException(string message, Exception inner)
    : Exception(message, inner);
