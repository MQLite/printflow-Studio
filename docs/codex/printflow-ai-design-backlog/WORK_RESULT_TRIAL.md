# Studio final PDF return trial

Run the independent receiver under the same Windows user as trial Studio. In the receiver's private configuration set `"studioPipe": "PrintFlow-WorkTrial"` and keep the approved file origins and the existing private tunnel for `/mcp`. Do not forward the control endpoints or the pipe.

Create a separate private `studio-work-result.local.json` outside the repository:

```json
{
  "pipeName": "PrintFlow-WorkTrial",
  "workspaceRoot": "C:/absolute/path/to/the/trial/workspace",
  "controlBearer": "<same control bearer as the receiver>",
  "readBearer": "<same read bearer as the receiver>"
}
```

Set `PRINTFLOW_WORK_RESULT_CONFIG` to that absolute file path in the shell that launches trial Studio. Use the newly built Studio and native host with the existing Chrome trial registration. With the variable unset, **自动生图 / Generate image** retains the submission-only behavior. When it is set, an unavailable receiver stops the attempt before Work submits anything.

For a trial with synthetic inputs, click **自动生图** once. Studio enrolls the attempt and includes a return instruction with that one submission. The PrintFlow Studio plugin must expose the save_final_pdf action. Work reviews and delivers its final one-page PDF. Studio checks the file and imports it automatically. Studio waits while it is open. Restarting with the same setup resumes up to eight recent enrolled attempts without starting a new conversation. Once the returned original is imported, select **继续处理 / Continue processing** on the card or in **AI 任务 / AI tasks** to open the existing processing session. A new session starts at **原图确认 / Original confirmation**. If Home is visible when the import completes, **继续待处理任务 / Continue processing** updates automatically. **停止接收 / Stop receiving** cancels the local pending request and attempts to revoke the receiver ticket; it does not stop Work from generating.

Update Studio, its native host, and the receiver together. Refresh the Work plugin actions to expose save_final_pdf before a live trial.

Before preparing the PDF for review, check that its first-page thumbnail appears on the Work result card and in **AI 任务 / AI tasks**. The task should already offer **继续处理 / Continue processing**.

On 17 November 2026, a synthetic QA17 → QA18 request recorded one upload and one confirmed Work submission. The returned 1254 × 1254 PNG imported using the saved attempt after restarting the updated Studio and receiver. Read-only database and file checks found one Prepare Design Asset session at Original Confirmation and one root Import revision matching the received file's SHA-256 and byte length. No second generation or Drive transfer was needed.

The final PDF trial used the same synthetic image, the updated native host and one confirmed Work submission. Work reviewed and returned a 947,956-byte single-page `QA18.pdf`. Studio automatically imported it. The AI Tasks view displayed a 192 × 192 thumbnail, and Continue processing opened the same session at Original confirmation.
