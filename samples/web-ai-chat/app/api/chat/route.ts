import { NextRequest } from 'next/server';
import { IronHiveCli, StreamChunk } from '@/lib/ironhive';

export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

/**
 * POST /api/chat
 * Streams chat response from ironhive CLI using --output jsonl.
 */
export async function POST(request: NextRequest) {
  const { prompt, sessionId, model, provider } = await request.json();

  if (!prompt) {
    return Response.json({ error: 'prompt is required' }, { status: 400 });
  }

  const cli = new IronHiveCli({
    sessionId,
    model,
    provider,
    executable: process.env.IRONHIVE_PATH || 'ironhive',
    cwd: process.env.IRONHIVE_CWD || process.cwd(),
  });

  // Transform CLI stream to SSE format
  const cliStream = cli.chatStream(prompt);
  const reader = cliStream.getReader();

  const encoder = new TextEncoder();
  const sseStream = new ReadableStream({
    async start(controller) {
      try {
        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          // Convert StreamChunk to SSE event
          const chunk = value as StreamChunk;
          let sseData: string;

          if (chunk.type === 'thinking' && chunk.content) {
            sseData = JSON.stringify({ thinkingDelta: chunk.content });
          } else if (chunk.type === 'text' && chunk.content) {
            sseData = JSON.stringify({ textDelta: chunk.content });
          } else if (chunk.type === 'tool_call') {
            sseData = JSON.stringify({
              toolCall: { id: chunk.id, name: chunk.name, arguments: chunk.arguments }
            });
          } else if (chunk.type === 'start') {
            sseData = JSON.stringify({ type: 'start', sessionId: chunk.sessionId });
          } else if (chunk.type === 'done') {
            sseData = JSON.stringify({ type: 'done', sessionId: chunk.sessionId });
          } else if (chunk.type === 'error') {
            sseData = JSON.stringify({ error: chunk.error });
          } else {
            continue;
          }

          controller.enqueue(encoder.encode(`data: ${sseData}\n\n`));
        }
        controller.enqueue(encoder.encode('data: [DONE]\n\n'));
      } catch (error) {
        controller.enqueue(encoder.encode(`data: ${JSON.stringify({ error: String(error) })}\n\n`));
      } finally {
        controller.close();
      }
    },
  });

  return new Response(sseStream, {
    headers: {
      'Content-Type': 'text/event-stream',
      'Cache-Control': 'no-cache',
      Connection: 'keep-alive',
    },
  });
}

/**
 * PUT /api/chat
 * Non-streaming version using --output json.
 */
export async function PUT(request: NextRequest) {
  const { prompt, sessionId, model, provider } = await request.json();

  if (!prompt) {
    return Response.json({ error: 'prompt is required' }, { status: 400 });
  }

  const cli = new IronHiveCli({
    sessionId,
    model,
    provider,
    executable: process.env.IRONHIVE_PATH || 'ironhive',
    cwd: process.env.IRONHIVE_CWD || process.cwd(),
  });

  const result = await cli.chat(prompt);
  return Response.json(result);
}
