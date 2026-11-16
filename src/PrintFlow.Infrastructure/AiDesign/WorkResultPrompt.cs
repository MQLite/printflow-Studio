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
            Review your work, then deliver the final result as a single-page PDF. Use PrintFlow Studio's save_final_pdf tool once with the finished PDF file.
            request_id: {request.RequestId:D}
            attempt_id: {request.AttemptId:D}
            upload_ticket: {ticket}
            Do not echo the upload ticket in your reply.
            """;
    }
}
