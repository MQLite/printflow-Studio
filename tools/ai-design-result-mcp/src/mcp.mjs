import { createMcpHandler, McpServer } from '@modelcontextprotocol/server';
import * as z from 'zod/v4';
import { validateSourceUrl, fetchSourceFile } from './source-fetch.mjs';

const fileSchema = z.strictObject({
  download_url: z.string(),
  file_id: z.string(),
  mime_type: z.string().optional(),
  file_name: z.string().optional()
});
const inputSchema = z.strictObject({
  request_id: z.string(), attempt_id: z.string(), upload_ticket: z.string(), file: fileSchema
});
const outputSchema = z.object({
  schemaVersion: z.number(), requestId: z.string(), attemptId: z.string(),
  sourceFileId: z.string(), objectId: z.string(), objectVersion: z.string(),
  byteLength: z.number(), sha256: z.string(), mediaType: z.string(),
  width: z.number(), height: z.number(), sourceVerified: z.boolean(),
  state: z.enum(['Ready', 'received_unverified'])
});

export function makeMcpHandler({ store, allowedSources, fileFetcher }) {
  return createMcpHandler(() => {
    const server = new McpServer({ name: 'printflow-ai-design-result', version: '0.1.0' });
    server.registerTool('save_image_result', {
      title: 'Save generated image result',
      description: 'Receives one already generated PNG or JPEG for an enrolled request attempt.',
      inputSchema, outputSchema,
      annotations: { readOnlyHint: false, destructiveHint: false, openWorldHint: true, idempotentHint: true },
      _meta: { 'openai/fileParams': ['file'] }
    }, async ({ request_id, attempt_id, upload_ticket, file }) => {
      try {
        validateSourceUrl(file, allowedSources);
        const receipt = await store.receive({ requestId: request_id, attemptId: attempt_id, uploadTicket: upload_ticket, file }, fileFetcher ?? (f => fetchSourceFile(f, { allowedSources })));
        return { structuredContent: receipt, content: [{ type: 'text', text: `${receipt.state}: ${receipt.objectId}` }] };
      } catch {
        return { isError: true, content: [{ type: 'text', text: 'Image result rejected.' }] };
      }
    });
    return server;
  }, { legacy: 'stateless', maxRequestBodySize: 65_536, maxSubscriptions: 0 });
}
