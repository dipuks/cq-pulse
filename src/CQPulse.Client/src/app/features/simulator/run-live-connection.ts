import { signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { RunProgress } from '../../core/models';

export type LiveState = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'unavailable';

/**
 * SignalR subscription to one run's progress. Live updates are an optimisation: the caller keeps polling REST,
 * and `resync` is invoked after every (re)connection so state missed while disconnected is reconciled.
 * Connection failures are surfaced through `state`/`detail`, never hidden.
 */
export class RunLiveConnection {
  readonly state = signal<LiveState>('idle');
  readonly detail = signal('');
  private connection?: HubConnection;

  constructor(private readonly hubUrl: string, private readonly accessToken: () => string | null) {}

  async watch(runId: string, onProgress: (progress: RunProgress) => void, resync: () => void): Promise<void> {
    await this.stop();
    const connection = new HubConnectionBuilder()
      .withUrl(this.hubUrl, { accessTokenFactory: () => this.accessToken() ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    this.connection = connection;

    connection.on('RunProgressUpdated', (progress: RunProgress) => {
      if (progress.runId === runId) onProgress(progress);
    });
    connection.onreconnecting(error => {
      this.state.set('reconnecting');
      this.detail.set(error?.message ?? '');
    });
    connection.onreconnected(async () => {
      try {
        await connection.invoke('JoinRun', runId);
        this.state.set('connected');
        this.detail.set('');
        resync();
      } catch (error) {
        this.fail(error);
      }
    });
    connection.onclose(error => {
      if (this.connection !== connection) return;
      if (error) this.fail(error); else this.state.set('idle');
    });

    this.state.set('connecting');
    try {
      await connection.start();
      await connection.invoke('JoinRun', runId);
      this.state.set('connected');
      this.detail.set('');
      resync();
    } catch (error) {
      if (this.connection === connection) this.fail(error);
    }
  }

  async stop(): Promise<void> {
    const connection = this.connection;
    this.connection = undefined;
    this.state.set('idle');
    if (connection) await connection.stop().catch(() => undefined);
  }

  private fail(error: unknown): void {
    this.state.set('unavailable');
    this.detail.set(error instanceof Error ? error.message : String(error));
  }
}
