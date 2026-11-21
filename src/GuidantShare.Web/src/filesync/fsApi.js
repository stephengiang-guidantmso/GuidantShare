// Calls to FileSync go through GuidantShare's own API (SuperAdmin only): same origin, session cookie, CSRF token on changes.
// FileSync itself is never reached from the browser.
import { csrfToken, resetCsrf } from '../api';

const BASE = '/api/filesync';

async function call(path, { method = 'GET', body } = {}) {
  const headers = {};
  if (method !== 'GET') headers['X-CSRF-TOKEN'] = await csrfToken();
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const res = await fetch(`${BASE}${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: 'same-origin'
  });
  if (res.status === 400 && method !== 'GET') resetCsrf();
  return res;
}

// FileSync answers errors as { message }; GuidantShare's own answers (401, 403, 502) use { error }
async function readJson(res) {
  return res.json().catch(() => ({}));
}
const errText = (data) => data.message || data.error || data.title;

export async function fetchHealth() {
  const res = await call('/health');
  return res.json();
}

export async function fetchWorkflows() {
  const res = await call('/workflows');
  return res.json();
}

// The plan/folder grouping the website uses, plus any jobs that are labelled wrongly
export async function fetchCatalog() {
  const res = await call('/catalog');
  if (!res.ok) throw new Error(`Could not load the website catalog (Status: ${res.status})`);
  return res.json();
}

// workflowId lets the backend use the job's saved password when the editor's password field was left blank
// Tests a job's connection: SFTP login, or sign-in to a web portal (endpointType other than 'Sftp')
export async function verifySftpConnection(sftpConfig, workflowId = null, endpointType = 'Sftp') {
  const params = new URLSearchParams({ endpointType });
  if (workflowId) params.set('workflowId', workflowId);
  const res = await call(`/sftp/verify?${params}`, { method: 'POST', body: sftpConfig });
  const data = await readJson(res);
  if (!res.ok || !data.success) {
    throw new Error(errText(data) || `Connection test failed (Status: ${res.status})`);
  }
  return data;
}

export async function createWorkflow(workflowData) {
  const res = await call('/workflows', { method: 'POST', body: workflowData });
  if (!res.ok) {
    throw new Error(errText(await readJson(res)) || `Failed to create workflow (${res.status})`);
  }
  return res.json();
}

export async function updateWorkflow(workflowId, workflowData) {
  const res = await call(`/workflows/${encodeURIComponent(workflowId)}`, { method: 'PUT', body: workflowData });
  if (!res.ok) {
    throw new Error(errText(await readJson(res)) || `Failed to update workflow (${res.status})`);
  }
  return res.json();
}

export async function deleteWorkflow(workflowId) {
  const res = await call(`/workflows/${encodeURIComponent(workflowId)}`, { method: 'DELETE' });
  if (!res.ok) {
    throw new Error(errText(await readJson(res)) || `Failed to delete workflow (${res.status})`);
  }
  return res.json();
}

export async function triggerWorkflowRun(workflowId, mode = 'Live') {
  const res = await call(`/workflows/${encodeURIComponent(workflowId)}/run`, { method: 'POST', body: { mode } });
  return res.json();
}

export async function toggleWorkflow(workflowId) {
  const res = await call(`/workflows/${encodeURIComponent(workflowId)}/toggle`, { method: 'POST' });
  return res.json();
}

export async function cancelWorkflowRun(workflowId) {
  const res = await call(`/workflows/${encodeURIComponent(workflowId)}/cancel`, { method: 'POST' });
  return res.json();
}

export async function fetchTransfers(workflowId = null, status = null, limit = 200, fromUtc = null, toUtc = null) {
  const params = new URLSearchParams({ limit: String(limit) });
  if (workflowId) params.set('workflowId', workflowId);
  if (status) params.set('status', status);
  if (fromUtc) params.set('fromUtc', fromUtc);
  if (toUtc) params.set('toUtc', toUtc);
  const res = await call(`/transfers?${params}`);
  return res.json();
}

// hours: 1, 12, 24 ... or null for all time
export async function fetchTransferCount(hours = null) {
  const res = await call(`/transfers/count${hours ? `?hours=${hours}` : ''}`);
  return res.json();
}

export async function retryTransfer(transferId) {
  const res = await call(`/transfers/${encodeURIComponent(transferId)}/retry`, { method: 'POST' });
  return res.json();
}

// Resets every failed / waiting / quarantined transfer and runs the affected jobs now
export async function retryAllTransfers() {
  const res = await call('/transfers/retry-all', { method: 'POST' });
  const data = await readJson(res);
  if (!res.ok) {
    throw new Error(errText(data) || `Retry All failed (Status: ${res.status})`);
  }
  return data;
}

export async function clearTransfers(status = null) {
  const res = await call(`/transfers${status ? `?status=${encodeURIComponent(status)}` : ''}`, { method: 'DELETE' });
  return res.json();
}

export async function fetchLogs() {
  const res = await call('/logs');
  return res.json();
}

// ---------------------------------------------------------------- Verify Sync
async function readJsonOrThrow(res, fallbackMessage) {
  const data = await readJson(res);
  if (!res.ok) {
    throw new Error(errText(data) || `${fallbackMessage} (Status: ${res.status})`);
  }
  return data;
}

export async function startVerification(workflowId) {
  const res = await call(`/workflows/${encodeURIComponent(workflowId)}/verify`, { method: 'POST' });
  return readJsonOrThrow(res, 'Could not start verification');
}

export async function fetchVerification(verificationId) {
  const res = await call(`/verifications/${encodeURIComponent(verificationId)}`);
  return readJsonOrThrow(res, 'Could not load verification');
}

// Returns null when the job has never been verified
export async function fetchLatestVerification(workflowId) {
  const res = await call(`/workflows/${encodeURIComponent(workflowId)}/verifications/latest`);
  if (res.status === 404) return null;
  return readJsonOrThrow(res, 'Could not load the last verification');
}

// A plain link: the browser sends the session cookie, so the CSV downloads like any file
export function verificationCsvUrl(verificationId) {
  return `${BASE}/verifications/${encodeURIComponent(verificationId)}/csv`;
}

export async function browseWorkflowFolder(workflowId, path = '') {
  const res = await call(`/workflows/${encodeURIComponent(workflowId)}/browse?path=${encodeURIComponent(path)}`);
  const data = await readJson(res);
  if (!res.ok) {
    throw new Error(errText(data) || `Could not open this folder (Status: ${res.status})`);
  }
  return data;
}

// A portal sign-in (SCAN) waiting for a one-time code to be typed in: { pending: { portal, prompt, requestedUtc, expiresUtc } | null }
export async function fetchPendingPortalCode() {
  const res = await call('/portal-code');
  if (!res.ok) return { pending: null };
  return res.json();
}

export async function submitPortalCode(code) {
  const res = await call('/portal-code', { method: 'POST', body: { code } });
  const data = await readJson(res);
  if (!res.ok) throw new Error(errText(data) || `Could not send the code (Status: ${res.status})`);
  return data;
}

// Gives up on the sign-in waiting for a code, so it fails now
export async function cancelPortalCode() {
  const res = await call('/portal-code', { method: 'DELETE' });
  if (!res.ok && res.status !== 404) throw new Error(`Could not cancel (Status: ${res.status})`);
}

// Log lines that mention one transfer's file (oldest first): [{ timestamp, level, workflowId, message }]
export async function fetchTransferLogs(transferId) {
  const res = await call(`/transfers/${encodeURIComponent(transferId)}/logs`);
  if (!res.ok) throw new Error(`Could not load logs (Status: ${res.status})`);
  return res.json();
}
