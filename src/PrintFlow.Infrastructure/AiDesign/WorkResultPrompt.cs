using PrintFlow.Domain.AiDesign;

namespace PrintFlow.Infrastructure.AiDesign;

public static class WorkResultPrompt
{
    public static bool IsTicket(string? ticket) => ticket is { Length: >= 8 and <= 100 } &&
        ticket.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    public static string Append(AiDesignRequest request, string ticket)
    {
        if (!IsTicket(ticket) || request.RequestId == Guid.Empty || request.AttemptId == Guid.Empty)
            throw new ArgumentException("Invalid Work result upload authority.");

        return $"""
            {request.Prompt.Text}

            PrintFlow Studio delivery:
            Generate exactly one image for this request. After generation completes, use PrintFlow Studio's save_image_result tool exactly once with the new original PNG or JPEG file object from this response. Use the actual file, not a reference attachment, preview, screenshot, URL, or re-encoded image. If the original file or tool is unavailable, report that return was unavailable; do not send another file.
            request_id: {request.RequestId:D}
            attempt_id: {request.AttemptId:D}
            upload_ticket: {ticket}
            Do not echo the upload ticket in your reply.
            """;
    }
}
