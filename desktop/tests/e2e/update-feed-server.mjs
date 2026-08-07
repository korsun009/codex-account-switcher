import { createReadStream, statSync } from 'node:fs'
import { createServer } from 'node:http'
import { extname, resolve, sep } from 'node:path'

const root = resolve(process.argv[2])
const port = Number(process.argv[3])

if (!Number.isInteger(port) || port < 1024 || port > 65535) {
  throw new Error('A valid test-server port is required.')
}

const contentTypes = new Map([
  ['.yml', 'text/yaml; charset=utf-8'],
  ['.blockmap', 'application/octet-stream'],
  ['.exe', 'application/octet-stream']
])

createServer((request, response) => {
  try {
    const pathname = decodeURIComponent(new URL(request.url ?? '/', 'http://127.0.0.1').pathname)
    const candidate = resolve(root, `.${pathname}`)
    if (candidate !== root && !candidate.startsWith(`${root}${sep}`)) {
      response.writeHead(403).end()
      return
    }
    const stat = statSync(candidate)
    if (!stat.isFile()) {
      response.writeHead(404).end()
      return
    }
    response.writeHead(200, {
      'Content-Type': contentTypes.get(extname(candidate)) ?? 'application/octet-stream',
      'Content-Length': stat.size,
      'Cache-Control': 'no-store'
    })
    createReadStream(candidate).pipe(response)
  } catch {
    response.writeHead(404).end()
  }
}).listen(port, '127.0.0.1', () => {
  process.stdout.write(`READY http://127.0.0.1:${port}/\n`)
})
