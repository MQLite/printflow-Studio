using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PrintFlow.Tests.Fixtures;

internal sealed record ResponsesImageRequest(string Prompt, string Background, string Format, string Size, byte[] Image);

internal static class ResponsesImageFixture
{
    public static async Task<ResponsesImageRequest> ReadAsync(HttpRequestMessage request, CancellationToken token)
    {
        request.RequestUri.ShouldBe(new Uri("https://api.openai.com/v1/responses"));
        request.Method.ShouldBe(HttpMethod.Post);
        request.Content!.Headers.ContentType!.MediaType.ShouldBe("application/json");
        using var json = JsonDocument.Parse(await request.Content.ReadAsByteArrayAsync(token));
        var root = json.RootElement;
        root.GetProperty("model").GetString().ShouldBe("gpt-6.2-sol");
        root.GetProperty("store").GetBoolean().ShouldBeTrue();
        root.GetProperty("max_tool_calls").GetInt32().ShouldBe(1);
        var tools = root.GetProperty("tools").EnumerateArray().ToArray();
        var choice = root.GetProperty("tool_choice");
        if (choice.ValueKind == JsonValueKind.String)
        {
            choice.GetString().ShouldBe("required");
            tools.Single(item => item.GetProperty("type").GetString() == "function")
                .GetProperty("name").GetString().ShouldBe("locate_printed_artwork");
        }
        else
        {
            choice.GetProperty("type").GetString().ShouldBe("image_generation");
            tools.Length.ShouldBe(1);
        }
        var tool = tools.Single(item => item.GetProperty("type").GetString() == "image_generation");
        tool.GetProperty("model").GetString().ShouldBe("gpt-image-2.6-sunburst");
        tool.GetProperty("action").GetString().ShouldBe("edit");

        var input = root.GetProperty("input").EnumerateArray().Single();
        input.GetProperty("role").GetString().ShouldBe("user");
        var items = input.GetProperty("content").EnumerateArray().ToArray();
        items.Length.ShouldBe(2);
        var image = items.Single(item => item.GetProperty("type").GetString() == "input_image");
        var data = image.GetProperty("image_url").GetString()!.Split(',', 2);
        data[0].ShouldMatch("^data:image/(png|jpeg);base64$");
        var prompt = items.Single(item => item.GetProperty("type").GetString() == "input_text");
        return new ResponsesImageRequest(prompt.GetProperty("text").GetString()!,
            tool.GetProperty("background").GetString()!, tool.GetProperty("output_format").GetString()!,
            tool.GetProperty("size").GetString()!, Convert.FromBase64String(data[1]));
    }

    public static HttpResponseMessage Reply(byte[]? png, string status = "completed",
        string imageStatus = "completed", int imageCount = 1)
    {
        var output = new List<object> { new { type = "reasoning" } };
        for (int i = 0; i < imageCount; i++)
        {
            output.Add(new
            {
                id = $"ig_test{i}",
                type = "image_generation_call",
                status = imageStatus,
                result = png is null ? null : Convert.ToBase64String(png),
                revised_prompt = "Private text recovered from an image.",
                url = "https://example.test/private",
            });
        }
        output.Add(new { type = "message", status = "completed" });
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                id = "resp_test42",
                status,
                output,
                usage = new { total_tokens = 12, input_tokens = 7, output_tokens = 5 },
            })),
        };
        response.Headers.Add("x-request-id", "req_test42");
        return response;
    }
}
