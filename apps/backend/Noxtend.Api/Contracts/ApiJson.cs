using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc;

namespace Noxtend.Api.Contracts;

public static class ApiJson
{
    public static void Configure(JsonOptions options)
        // JSON 응답의 한국어 escape 비용 절감
        => options.JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
}
