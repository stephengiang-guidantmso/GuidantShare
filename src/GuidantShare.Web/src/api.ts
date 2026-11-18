let csrf: string | null = null

async function token() {
  if (!csrf) csrf = (await (await fetch('/api/auth/csrf', { credentials: 'same-origin' })).json()).token
  return csrf!
}

export class ApiError extends Error {
  status: number
  code?: string
  constructor(status: number, message: string, code?: string) { super(message); this.status = status; this.code = code }
}

export async function api<T = any>(path: string, opts: { method?: string; body?: unknown; form?: FormData } = {}): Promise<T> {
  const method = opts.method ?? 'GET'
  const headers: Record<string, string> = {}
  if (method !== 'GET') headers['X-CSRF-TOKEN'] = await token()
  let body: BodyInit | undefined
  if (opts.form) body = opts.form
  else if (opts.body !== undefined) { headers['Content-Type'] = 'application/json'; body = JSON.stringify(opts.body) }
  const res = await fetch(path, { method, headers, body, credentials: 'same-origin' })
  if ((res.status === 400 && method !== 'GET') || path === '/api/auth/login' || path === '/api/auth/logout') csrf = null
  const text = await res.text()
  const data = text ? JSON.parse(text) : null
  if (!res.ok) throw new ApiError(res.status, data?.error ?? `Request failed (${res.status})`, data?.code)
  return data as T
}

export const fmtSize = (n: number) =>
  n < 1024 ? `${n} B` : n < 1048576 ? `${(n / 1024).toFixed(1)} KB` : n < 1073741824 ? `${(n / 1048576).toFixed(1)} MB` : `${(n / 1073741824).toFixed(2)} GB`
export const fmtDate = (s?: string | null) => (s ? new Date(s).toLocaleString() : '')
