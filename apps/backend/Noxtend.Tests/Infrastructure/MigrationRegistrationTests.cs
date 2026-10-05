using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Infrastructure.Persistence;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 마이그레이션이 EF 에 실제로 보이는가.
///
/// **손으로 쓴 마이그레이션이 조용히 무시된 적이 있다** (occludedby-recompute, 2026-09-01).
/// EF 는 <see cref="MigrationAttribute"/> 가 붙은 타입만 마이그레이션으로 인식하는데,
/// 그 속성은 `dotnet ef migrations add` 가 만드는 `.Designer.cs` 에 있다. 파일을 직접
/// 쓰면 `partial` 짝이 없어도 컴파일이 통과해 빌드·테스트가 전부 초록불인 채로
/// **DB 에는 아무것도 적용되지 않는다.**
///
/// 그때 빠진 것은 프롬프트 시드였고, 결과는 "활성 프롬프트가 없습니다" 로 작업 전체 실패였다.
/// </summary>
public sealed class MigrationRegistrationTests
{
    [Fact]
    public void EveryMigrationIsVisibleToEf()
    {
        var declared = typeof(NoxtendDbContext).Assembly
            .GetTypes()
            .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract)
            .ToArray();

        // 하한이 없으면 타입 조회가 빈 배열을 내도 이 테스트가 조용히 통과한다
        Assert.True(declared.Length > 30, $"마이그레이션을 {declared.Length}개만 찾았다");

        var invisible = declared
            .Where(t => t.GetCustomAttribute<MigrationAttribute>() is null)
            .Select(t => t.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(invisible);
    }
}
