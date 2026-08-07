import { randomUUID } from 'node:crypto'
import { spawn, type ChildProcessWithoutNullStreams } from 'node:child_process'
import { app } from 'electron'
import { existsSync } from 'node:fs'
import { join } from 'node:path'
import { createInterface } from 'node:readline'
import type { BackendCommand, BridgeRequest, BridgeResponse } from '../shared/contracts'
import { backendCommandTimeout } from '../shared/timeouts'

interface PendingRequest {
  resolve: (response: BridgeResponse) => void
  timeout: NodeJS.Timeout
}

export class BackendClient {
  private child: ChildProcessWithoutNullStreams | null = null
  private readonly pending = new Map<string, PendingRequest>()
  private disposed = false

  async invoke<T>(command: BackendCommand, payload?: unknown): Promise<BridgeResponse<T>> {
    this.ensureStarted()
    const id = randomUUID()
    const request: BridgeRequest = { id, command, payload }

    return await new Promise<BridgeResponse<T>>((resolve) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id)
        resolve({ id, ok: false, data: null, error: 'Backend request timed out.' })
      }, backendCommandTimeout(command))

      this.pending.set(id, { resolve: resolve as (response: BridgeResponse) => void, timeout })
      this.child?.stdin.write(`${JSON.stringify(request)}\n`)
    })
  }

  dispose(): void {
    this.disposed = true
    for (const [id, pending] of this.pending) {
      clearTimeout(pending.timeout)
      pending.resolve({ id, ok: false, data: null, error: 'Desktop backend stopped.' })
    }
    this.pending.clear()
    this.child?.kill()
    this.child = null
  }

  private ensureStarted(): void {
    if (this.disposed) {
      throw new Error('Desktop backend is disposed.')
    }
    if (this.child && !this.child.killed) {
      return
    }

    const executable = this.resolveExecutable()
    if (!existsSync(executable)) {
      throw new Error(`Desktop backend is missing: ${executable}`)
    }

    this.child = spawn(executable, ['--desktop-bridge'], {
      windowsHide: true,
      stdio: ['pipe', 'pipe', 'pipe']
    })

    this.child.stdin.setDefaultEncoding('utf8')
    this.child.stdout.setEncoding('utf8')
    const lines = createInterface({ input: this.child.stdout })
    lines.on('line', (line) => this.handleLine(line))
    this.child.stderr.resume()
    this.child.once('exit', () => this.handleExit())
    this.child.once('error', () => this.handleExit())
  }

  private handleLine(line: string): void {
    if (line.length > 2_000_000) {
      return
    }

    let response: BridgeResponse
    try {
      response = JSON.parse(line) as BridgeResponse
    } catch {
      return
    }

    if (!response || typeof response.id !== 'string' || typeof response.ok !== 'boolean') {
      return
    }

    const pending = this.pending.get(response.id)
    if (!pending) {
      return
    }
    clearTimeout(pending.timeout)
    this.pending.delete(response.id)
    pending.resolve(response)
  }

  private handleExit(): void {
    this.child = null
    for (const [id, pending] of this.pending) {
      clearTimeout(pending.timeout)
      pending.resolve({ id, ok: false, data: null, error: 'Desktop backend exited unexpectedly.' })
    }
    this.pending.clear()
  }

  private resolveExecutable(): string {
    const override = process.env.CODEX_SWITCHER_BACKEND
    if (!app.isPackaged && override) {
      return override
    }
    return join(process.resourcesPath, 'backend', 'CodexAccountSwitcher.exe')
  }
}
