import { createMcpHandler, McpServer } from '@modelcontextprotocol/server';
import * as z from 'zod/v4';
import { validateSourceUrl, fetchSourceFile } from './source-fetch.mjs';
import { PDF_MEDIA_TYPE } from './image-validation.mjs';

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
  width: z.number().nullable(), height: z.number().nullable(), sourceVerified: z.boolean(),
  state: z.enum(['Ready', 'received_unverified']), workConversationId: z.string().optional()
});

export function makeMcpHandler({ store, allowedSources, fileFetcher, observeSource, reportSourceFailure }) {
  return createMcpHandler(() => {
    const server = new McpServer({ name: 'printflow-ai-design-result', version: '0.1.0' });
    const register = (name, title, description, returnMediaType) => server.registerTool(name, {
      title, description,
      inputSchema, outputSchema,
      annotations: { readOnlyHint: false, destructiveHint: false, openWorldHint: true, idempotentHint: true },
      _meta: { 'openai/fileParams': ['file'] }
    }, async ({ request_id, attempt_id, upload_ticket, file }) => {
      try {
        const receipt = await store.receive({
          requestId: request_id, attemptId: attempt_id, uploadTicket: upload_ticket, file, returnMediaType
        }, async source => {
          if (observeSource) {
            const url = new URL(source.download_url);
            if (url.protocol === 'https:' && !url.username && !url.password && !url.hash) {
              const observed = await observeSource({
                requestId: request_id, attemptId: attempt_id,
                sourceFileId: source.file_id, origin: url.origin, path: url.pathname
              });
              if (observed !== false) throw new Error('Source observation only.');
            }
          }
          validateSourceUrl(source, allowedSources);
          return fileFetcher ? fileFetcher(source) : fetchSourceFile(source, { allowedSources, returnMediaType });
        });
        return { structuredContent: receipt, content: [{ type: 'text', text: `${receipt.state}: ${receipt.objectId}` }] };
      } catch (error) {
        if (reportSourceFailure) {
          const safe = /^(?:source (?:identity mismatch|bytes mismatch|URL denied|address denied|response denied \(\d{3}\)|timeout|length mismatch)|upload unauthorized|attempt not current)$/;
          const code = safe.test(error?.message) ? error.message : 'source verification failed';
          reportSourceFailure({ requestId: request_id, attemptId: attempt_id, code });
        }
        return { isError: true, content: [{ type: 'text', text: returnMediaType === PDF_MEDIA_TYPE ? 'Final PDF rejected.' : 'Image result rejected.' }] };
      }
    });
    register('save_image_result', 'Save generated image result',
      'Receives one PNG or JPEG for an enrolled legacy image request.');
    register('save_final_pdf', 'Deliver final PDF',
      'Returns the finalized single-page PDF to the enrolled PrintFlow Studio request after Work finishes its review.', PDF_MEDIA_TYPE);
    return server;
  }, { legacy: 'stateless', maxRequestBodySize: 65_536, maxSubscriptions: 0 });
}
