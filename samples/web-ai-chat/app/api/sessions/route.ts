import { IronHiveCli } from '@/lib/ironhive';

export const runtime = 'nodejs';

/**
 * GET /api/sessions
 * List available sessions using sessions list --output json.
 */
export async function GET() {
  const cli = new IronHiveCli({
    executable: process.env.IRONHIVE_PATH || 'ironhive',
    cwd: process.env.IRONHIVE_CWD || process.cwd(),
  });

  try {
    const sessions = await cli.listSessions(20);
    return Response.json({ sessions });
  } catch (error) {
    return Response.json(
      { error: String(error), sessions: [] },
      { status: 500 }
    );
  }
}
