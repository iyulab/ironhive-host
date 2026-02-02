import { REMAINING_GAPS } from '@/lib/ironhive';

/**
 * GET /api/gaps
 * Return remaining CLI gaps after --output json/jsonl implementation.
 */
export async function GET() {
  const implementedGaps = [
    { id: 'CLI-G1', description: '--output json flag for structured output', status: 'implemented' },
    { id: 'CLI-G2', description: 'sessions list --output json for machine-readable list', status: 'implemented' },
    { id: 'CLI-G3', description: 'JSON lines format for streaming output (--output jsonl)', status: 'implemented' },
  ];

  return Response.json({
    description: 'CLI gaps for programmatic usage',
    implemented: implementedGaps,
    remaining: REMAINING_GAPS,
    summary: {
      implemented: implementedGaps.length,
      remaining: REMAINING_GAPS.filter(g => g.status !== 'resolved').length,
    },
  });
}
