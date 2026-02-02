/**
 * IronHive CLI wrapper for subprocess integration.
 *
 * Uses the new --output json/jsonl options for structured output.
 */

import { spawn, ChildProcess } from 'child_process';
import { EventEmitter } from 'events';

export interface IronHiveOptions {
  /** Path to ironhive executable (default: 'ironhive') */
  executable?: string;
  /** Working directory for the CLI */
  cwd?: string;
  /** Model to use */
  model?: string;
  /** Provider (openai, ollama, gpustack) */
  provider?: string;
  /** Session ID to resume */
  sessionId?: string;
}

export interface ChatResponse {
  content: string;
  sessionId?: string;
  usage?: {
    inputTokens: number;
    outputTokens: number;
    totalTokens: number;
  };
  thinking?: {
    content: string;
    tokenCount?: number;
  };
  toolCalls?: Array<{
    name: string;
    arguments: string;
    result: string;
    success: boolean;
  }>;
}

export interface StreamChunk {
  type: 'start' | 'text' | 'thinking' | 'tool_call' | 'done' | 'error';
  content?: string;
  sessionId?: string;
  id?: string;
  name?: string;
  arguments?: string;
  error?: string;
}

export interface SessionInfo {
  id: string;
  status: string;
  model: string;
  created: string;
  messageCount: number;
  firstMessage?: string;
}

export class IronHiveCli extends EventEmitter {
  private options: IronHiveOptions;
  private process: ChildProcess | null = null;

  constructor(options: IronHiveOptions = {}) {
    super();
    this.options = {
      executable: options.executable || 'ironhive',
      cwd: options.cwd || process.cwd(),
      ...options,
    };
  }

  /**
   * Run a single prompt and get JSON response.
   * Uses --output json for structured output.
   */
  async chat(prompt: string): Promise<ChatResponse> {
    const args = this.buildArgs(prompt, 'json');

    return new Promise((resolve, reject) => {
      const proc = spawn(this.options.executable!, args, {
        cwd: this.options.cwd,
        shell: true,
      });

      let stdout = '';
      let stderr = '';

      proc.stdout?.on('data', (data) => {
        stdout += data.toString();
      });

      proc.stderr?.on('data', (data) => {
        stderr += data.toString();
      });

      proc.on('close', (code) => {
        if (code === 0) {
          try {
            const response = JSON.parse(stdout) as ChatResponse;
            resolve(response);
          } catch {
            resolve({ content: stdout });
          }
        } else {
          try {
            const error = JSON.parse(stdout);
            resolve({ content: '', ...error });
          } catch {
            resolve({ content: '', sessionId: undefined });
          }
        }
      });

      proc.on('error', (err) => {
        reject(err);
      });
    });
  }

  /**
   * Run a prompt with streaming JSON Lines output.
   * Uses --output jsonl for structured streaming.
   */
  chatStream(prompt: string): ReadableStream<StreamChunk> {
    const args = this.buildArgs(prompt, 'jsonl');

    return new ReadableStream<StreamChunk>({
      start: (controller) => {
        const proc = spawn(this.options.executable!, args, {
          cwd: this.options.cwd,
          shell: true,
        });

        this.process = proc;
        let buffer = '';

        proc.stdout?.on('data', (data) => {
          buffer += data.toString();
          const lines = buffer.split('\n');
          buffer = lines.pop() || ''; // Keep incomplete line in buffer

          for (const line of lines) {
            if (line.trim()) {
              try {
                const chunk = JSON.parse(line) as StreamChunk;
                controller.enqueue(chunk);
              } catch {
                // Invalid JSON line, skip
              }
            }
          }
        });

        proc.stderr?.on('data', (data) => {
          console.error('[ironhive stderr]', data.toString());
        });

        proc.on('close', () => {
          // Process remaining buffer
          if (buffer.trim()) {
            try {
              const chunk = JSON.parse(buffer) as StreamChunk;
              controller.enqueue(chunk);
            } catch {
              // Invalid JSON, skip
            }
          }
          controller.close();
          this.process = null;
        });

        proc.on('error', (err) => {
          controller.error(err);
          this.process = null;
        });
      },
      cancel: () => {
        this.abort();
      },
    });
  }

  /**
   * List sessions with JSON output.
   * Uses sessions list --output json.
   */
  async listSessions(limit: number = 10): Promise<SessionInfo[]> {
    return new Promise((resolve, reject) => {
      const args = ['sessions', 'list', '-n', limit.toString(), '--output', 'json'];

      const proc = spawn(this.options.executable!, args, {
        cwd: this.options.cwd,
        shell: true,
      });

      let stdout = '';

      proc.stdout?.on('data', (data) => {
        stdout += data.toString();
      });

      proc.on('close', (code) => {
        if (code === 0) {
          try {
            const sessions = JSON.parse(stdout) as SessionInfo[];
            resolve(sessions);
          } catch {
            resolve([]);
          }
        } else {
          resolve([]);
        }
      });

      proc.on('error', reject);
    });
  }

  /**
   * Abort the current running process.
   */
  abort(): void {
    if (this.process) {
      this.process.kill('SIGINT');
      this.process = null;
    }
  }

  private buildArgs(prompt: string, outputFormat: 'json' | 'jsonl'): string[] {
    const args: string[] = ['-p', `"${prompt.replace(/"/g, '\\"')}"`, '--output', outputFormat];

    if (this.options.model) {
      args.push('-m', this.options.model);
    }

    if (this.options.provider) {
      args.push('--provider', this.options.provider);
    }

    if (this.options.sessionId) {
      args.push('-r', this.options.sessionId);
    }

    return args;
  }
}

/**
 * Remaining Gaps (after CLI-G1, G2, G3 implementation):
 *
 * Gap CLI-G4: Session ID in response - Now included in JSON output
 * Gap CLI-G5: --plain flag - May still be useful for non-JSON text output
 * Gap CLI-G6: stdin prompt support - For long prompts
 * Gap CLI-G7: Exit codes documentation - For error handling
 */
export const REMAINING_GAPS = [
  { id: 'CLI-G4', description: 'Session ID now included via --output json', status: 'resolved' },
  { id: 'CLI-G5', description: '--plain flag for non-JSON text output', priority: 'medium' },
  { id: 'CLI-G6', description: 'Read prompt from stdin (--prompt -)', priority: 'low' },
  { id: 'CLI-G7', description: 'Documented exit codes for errors', priority: 'low' },
];
