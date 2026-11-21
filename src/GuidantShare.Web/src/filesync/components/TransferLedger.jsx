import React, { useState, useEffect } from 'react';
import { fetchTransfers } from '../fsApi';
import { useConfirmClick } from './confirmClick';
import Icon from './Icon';

const STATUS_DESCRIPTIONS = {
  'Completed': 'Transferred: File successfully synced to destination, SHA-256 verified, and preserved in 30-day staging archive.',
  'Deleted': 'Deleted / Purged: Replica safely purged from destination container during 1:1 Mirror Sync after source file was renamed or deleted.',
  'Failed': 'Failed: Transfer encountered a terminal error or exceeded maximum configured retry attempts.',
  'WaitingRetry': 'Waiting Retry: Transient network or connection failure; currently queued in exponential backoff awaiting the next retry cycle.',
  'Quarantined': 'Quarantined: File transferred but failed integrity or checksum validation; isolated to protect system data.',
  'Transferring': 'Transferring: Active payload stream in progress between source and destination endpoints.',
  'Staged': 'Staged: File saved in local working directory awaiting cryptographic hash computation and transfer dispatch.',
  'Discovered': 'Discovered: Source file identified by change detection engine.',
  'WaitingStability': 'Waiting Stability: File detected at source and settling to ensure no active write locks or size changes.'
};

const STATUS_OPTIONS = [
  { value: 'Completed', label: 'Completed' },
  { value: 'Deleted', label: 'Deleted' },
  { value: 'Failed', label: 'Failed' },
  { value: 'WaitingRetry', label: 'Waiting Retry' },
  { value: 'Quarantined', label: 'Quarantined' },
  { value: 'Transferring', label: 'Transferring' },
  { value: 'Staged', label: 'Staged' }
];

const FILTERED_FETCH_LIMIT = 1000;

// <input type="date"> gives "YYYY-MM-DD"; convert to the start/end of that day in local time, as UTC ISO
const toStartOfDayUtc = (value) => {
  if (!value) return null;
  const [y, m, d] = value.split('-').map(Number);
  return new Date(y, m - 1, d, 0, 0, 0, 0).toISOString();
};

const toEndOfDayUtc = (value) => {
  if (!value) return null;
  const [y, m, d] = value.split('-').map(Number);
  return new Date(y, m - 1, d, 23, 59, 59, 999).toISOString();
};

// initialJobFilter: job id to filter by when opened from a job's "files transferred" link
export default function TransferLedger({ transfers, workflows = [], onSelectTransfer, onClearTransfers, onRefresh, initialJobFilter = '' }) {
  const [jobFilter, setJobFilter] = useState(initialJobFilter);
  const [clearArmed, confirmClear] = useConfirmClick();
  const [statusFilter, setStatusFilter] = useState('');
  const [dateFrom, setDateFrom] = useState('');
  const [dateTo, setDateTo] = useState('');
  const [filteredResults, setFilteredResults] = useState(null);

  const hasFilters = Boolean(jobFilter || statusFilter || dateFrom || dateTo);

  // With any filter active, query the server so results are not limited to the latest page App.jsx polls.
  // Re-runs whenever the parent refreshes `transfers`, so filtered views stay live too.
  useEffect(() => {
    if (!hasFilters) {
      setFilteredResults(null);
      return;
    }

    let cancelled = false;
    fetchTransfers(jobFilter || null, statusFilter || null, FILTERED_FETCH_LIMIT, toStartOfDayUtc(dateFrom), toEndOfDayUtc(dateTo))
      .then(data => { if (!cancelled) setFilteredResults(Array.isArray(data) ? data : []); })
      .catch(() => { if (!cancelled) setFilteredResults([]); });

    return () => { cancelled = true; };
  }, [jobFilter, statusFilter, dateFrom, dateTo, transfers, hasFilters]);

  const filteredTransfers = hasFilters ? (filteredResults || []) : transfers;

  const clearFilters = () => {
    setJobFilter('');
    setStatusFilter('');
    setDateFrom('');
    setDateTo('');
  };

  const workflowNames = Object.fromEntries((workflows || []).map(wf => [wf.workflowId, wf.name]));

  const formatBytes = (bytes) => {
    if (!bytes || bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  };

  const formatTime = (iso) => {
    if (!iso) return '-';
    const d = new Date(iso);
    const date = d.toLocaleDateString([], { month: 'short', day: 'numeric' });
    const time = d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    return `${date} ${time}`;
  };

  return (
    <div id="ledger-section" style={{ marginBottom: '28px' }}>
      <div className="section-header">
        <div className="section-title">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
            <polyline points="14 2 14 8 20 8"></polyline>
            <line x1="16" y1="13" x2="8" y2="13"></line>
            <line x1="16" y1="17" x2="8" y2="17"></line>
            <polyline points="10 9 9 9 8 9"></polyline>
          </svg>
          <span>DURABLE TRANSFER LEDGER (SQLITE WAL)</span>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
          {transfers.length > 0 && (
            <button
              id="btn-clear-ledger-history"
              className={`btn btn-sm${clearArmed ? '' : ' btn-icon'}`}
              style={{ background: 'rgba(239, 68, 68, 0.1)', color: 'var(--red-text)', border: '1px solid var(--red-border)', fontSize: '0.78rem', ...(clearArmed ? { padding: '4px 10px' } : {}) }}
              onClick={() => confirmClear(() => onClearTransfers && onClearTransfers())}
              title={clearArmed ? 'Click again to clear the transfer history' : 'Clear transfer history'}
              aria-label={clearArmed ? 'Confirm clearing the transfer history' : 'Clear transfer history'}
            >
              {clearArmed ? 'Confirm?' : <Icon name="trash" />}
            </button>
          )}
        </div>
      </div>

      <div className="ledger-filter-bar" id="ledger-filter-bar">
        <label className="ledger-filter-field">
          <span>Job</span>
          <select id="ledger-filter-job" value={jobFilter} onChange={e => setJobFilter(e.target.value)}>
            <option value="">All Jobs</option>
            {(workflows || []).map(wf => (
              <option key={wf.workflowId} value={wf.workflowId}>{wf.name}</option>
            ))}
          </select>
        </label>

        <label className="ledger-filter-field">
          <span>From</span>
          <input id="ledger-filter-from" type="date" value={dateFrom} max={dateTo || undefined} onChange={e => setDateFrom(e.target.value)} />
        </label>

        <label className="ledger-filter-field">
          <span>To</span>
          <input id="ledger-filter-to" type="date" value={dateTo} min={dateFrom || undefined} onChange={e => setDateTo(e.target.value)} />
        </label>

        <label className="ledger-filter-field">
          <span>Status</span>
          <select id="ledger-filter-status" value={statusFilter} onChange={e => setStatusFilter(e.target.value)}>
            <option value="">All Statuses</option>
            {STATUS_OPTIONS.map(opt => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
        </label>

        <div className="ledger-filter-summary">
          <span>{filteredTransfers.length} record{filteredTransfers.length === 1 ? '' : 's'}</span>
          {hasFilters && (
            <button id="ledger-filter-clear" className="btn btn-secondary btn-sm" onClick={clearFilters}>
              Clear Filters
            </button>
          )}
        </div>
      </div>

      <div className="content-card">
        <div className="table-container">
          <table id="ledger-table">
            <thead>
              <tr>
                <th>Target Filename & Checksum</th>
                <th>Workflow ID</th>
                <th>Size</th>
                <th>Transfer State</th>
                <th>Started</th>
                <th>Details</th>
              </tr>
            </thead>
            <tbody>
              {filteredTransfers.map((t) => (
                <tr key={t.transferId} id={`transfer-row-${t.transferId}`}>
                  <td>
                    <div style={{ fontWeight: 600, color: 'var(--text-main)', fontFamily: 'var(--font-mono)', fontSize: '0.83rem' }}>
                      {t.filename}
                    </div>
                    <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontFamily: 'var(--font-mono)' }}>
                      SHA-256: {t.sha256Hash ? `${t.sha256Hash.substring(0, 16)}...` : (t.status === 'Deleted' ? 'Purged from destination' : 'Computed on stage')}
                    </div>
                  </td>
                  <td style={{ fontSize: '0.82rem', color: 'var(--text-muted)', fontFamily: 'var(--font-mono)' }}>
                    <div>{workflowNames[t.workflowId] || t.workflowId}</div>
                    {workflowNames[t.workflowId] && (
                      <div style={{ fontSize: '0.72rem' }}>{t.workflowId}</div>
                    )}
                  </td>
                  <td style={{ fontSize: '0.82rem', fontFamily: 'var(--font-mono)' }}>
                    {formatBytes(t.fileSizeBytes)}
                  </td>
                  <td>
                    <span 
                      className={`status-badge ${
                        t.status === 'Completed' ? 'badge-nominal' :
                        t.status === 'Deleted' ? 'badge-deleted' :
                        t.status === 'Failed' ? 'badge-critical' : 'badge-warning'
                      }`}
                      title={STATUS_DESCRIPTIONS[t.status] || `Transfer State: ${t.status}`}
                      style={{ cursor: 'help' }}
                    >
                      {t.status}
                    </span>
                  </td>
                  <td style={{ fontSize: '0.82rem', color: 'var(--text-muted)', fontFamily: 'var(--font-mono)' }}>
                    {formatTime(t.startedUtc)}
                  </td>
                  <td>
                    <button
                      id={`btn-inspect-${t.transferId}`}
                      className="btn btn-secondary btn-sm"
                      onClick={() => onSelectTransfer(t)}
                    >
                      Audit Trail
                    </button>
                  </td>
                </tr>
              ))}
              {filteredTransfers.length === 0 && (
                <tr>
                  <td colSpan={6} style={{ textAlign: 'center', color: 'var(--text-muted)', fontSize: '0.85rem', padding: '20px' }}>
                    {hasFilters ? 'No transfers match these filters.' : 'No transfers recorded yet.'}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
