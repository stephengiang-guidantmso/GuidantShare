import React, { useState, useEffect } from 'react';
import { useConfirmClick } from './confirmClick';
import Icon from './Icon';

// A retry counts as finished when its row leaves the queue (success) or its attempt count goes up (failed again).
// Safety net: once the job is no longer running and nothing has changed for this long, stop showing the spinner.
const RETRY_SETTLE_MS = 20000;

export default function RetryQueue({ transfers, workflows = [], onRetryTransfer, onRetryAll, onSelectTransfer, onClearRetries, onRefresh }) {
  const retryItems = transfers.filter(t => t.status === 'WaitingRetry' || t.status === 'Failed' || t.status === 'Quarantined');
  // transferId -> { attempt, since, fromAll }: retries clicked and not finished yet
  const [retrying, setRetrying] = useState({});
  const [isRetryingAll, setIsRetryingAll] = useState(false);
  const [clearArmed, confirmClear] = useConfirmClick();
  const [retryAllMessage, setRetryAllMessage] = useState('');

  const markRetrying = (items, fromAll = false) => {
    const now = Date.now();
    setRetrying(prev => {
      const next = { ...prev };
      items.forEach(item => { next[item.transferId] = { attempt: item.attemptCount, since: now, fromAll }; });
      return next;
    });
  };

  // Drop finished retries whenever fresh data arrives
  useEffect(() => {
    setRetrying(prev => {
      const ids = Object.keys(prev);
      if (ids.length === 0) return prev;
      const now = Date.now();
      const next = {};
      ids.forEach(id => {
        const item = retryItems.find(t => t.transferId === id);
        if (!item || item.attemptCount !== prev[id].attempt) return; // left the queue, or the attempt finished
        const jobRunning = workflows.some(w => w.workflowId === item.workflowId && w.isRunning);
        if (!jobRunning && now - prev[id].since > RETRY_SETTLE_MS) return;
        next[id] = prev[id];
      });
      return Object.keys(next).length === ids.length ? prev : next;
    });
  }, [transfers, workflows]);

  // Refresh more often while a retry is in progress, so the spinner clears promptly
  const anyRetrying = Object.keys(retrying).length > 0;
  // Retry All keeps its spinner until every retry it started has finished
  const retryAllBusy = isRetryingAll || Object.values(retrying).some(r => r.fromAll);
  useEffect(() => {
    if (!anyRetrying || !onRefresh) return;
    const interval = setInterval(onRefresh, 3000);
    return () => clearInterval(interval);
  }, [anyRetrying]);

  const handleRetry = async (item) => {
    markRetrying([item]);
    try {
      await onRetryTransfer(item.transferId);
    } catch {
      setRetrying(prev => {
        const next = { ...prev };
        delete next[item.transferId];
        return next;
      });
    }
  };

  const handleRetryAll = async () => {
    if (!onRetryAll) return;
    markRetrying(retryItems, true);
    setIsRetryingAll(true);
    setRetryAllMessage('');
    try {
      const result = await onRetryAll();
      setRetryAllMessage(result?.message || 'Retry started.');
    } catch (err) {
      setRetryAllMessage(err.message || 'Retry All failed.');
    } finally {
      setIsRetryingAll(false);
    }
  };

  return (
    <div id="retry-section" style={{ marginBottom: '28px' }}>
      <div className="section-header">
        <div className="section-title">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
            <line x1="12" y1="9" x2="12" y2="13"></line>
            <line x1="12" y1="17" x2="12.01" y2="17"></line>
          </svg>
          <span>ACTIVE RETRY & QUARANTINE QUEUE ({retryItems.length})</span>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
          {retryAllMessage && (
            <span style={{ fontSize: '0.78rem', color: 'var(--text-muted)' }}>{retryAllMessage}</span>
          )}
          {retryItems.length > 0 && (
            <button
              id="btn-retry-all"
              className="btn btn-primary btn-sm btn-icon"
              disabled={retryAllBusy}
              onClick={handleRetryAll}
              title={retryAllBusy ? 'Retrying...' : 'Retry all: every failed, waiting, and quarantined file now'}
              aria-label={retryAllBusy ? 'Retrying all' : 'Retry all'}
              style={{ width: '36px' }}
            >
              {retryAllBusy ? <span className="btn-spinner" aria-hidden="true" /> : <Icon name="retryAll" />}
            </button>
          )}
          {retryItems.length > 0 && (
            <button
              id="btn-clear-retry-queue"
              className={`btn btn-sm${clearArmed ? '' : ' btn-icon'}`}
              style={{ background: 'rgba(239, 68, 68, 0.1)', color: 'var(--red-text)', border: '1px solid var(--red-border)', fontSize: '0.78rem', ...(clearArmed ? { padding: '4px 10px' } : { width: '36px' }) }}
              onClick={() => confirmClear(() => onClearRetries && onClearRetries())}
              title={clearArmed ? 'Click again to clear the queue' : 'Clear queue: remove all failed and pending retries'}
              aria-label={clearArmed ? 'Confirm clearing the queue' : 'Clear queue'}
            >
              {clearArmed ? 'Confirm?' : <Icon name="trash" />}
            </button>
          )}
        </div>
      </div>

      {retryItems.length === 0 ? (
        <div className="content-card" style={{ padding: '28px', textAlign: 'center' }}>
          <div style={{ display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: '48px', height: '48px', borderRadius: '12px', background: 'var(--green-bg)', color: 'var(--green-text)', marginBottom: '12px' }}>
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
              <polyline points="20 6 9 17 4 12"></polyline>
            </svg>
          </div>
          <div style={{ fontSize: '1.05rem', fontWeight: 700, color: 'var(--green-text)' }}>
            Retry Queue is Nominal
          </div>
          <div style={{ fontSize: '0.82rem', color: 'var(--text-muted)', marginTop: '4px' }}>
            All active transfers have verified and zero files are held in quarantine or exponential backoff.
          </div>
        </div>
      ) : (
        <div className="content-card">
          <div className="table-container">
            <table id="retry-table">
              <thead>
                <tr>
                  <th>Filename</th>
                  <th>Workflow ID</th>
                  <th>Attempt Status</th>
                  <th>Fault Classification & Message</th>
                  <th>Next Scheduled Attempt</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {retryItems.map(item => (
                  <tr key={item.transferId} id={`retry-row-${item.transferId}`}>
                    <td style={{ fontFamily: 'var(--font-mono)', fontWeight: 700, color: 'var(--text-main)', fontSize: '0.83rem' }}>
                      {item.filename}
                    </td>
                    <td style={{ color: 'var(--text-muted)', fontFamily: 'var(--font-mono)', fontSize: '0.82rem' }}>
                      {item.workflowId}
                    </td>
                    <td>
                      <span className={`status-badge ${item.status === 'Quarantined' ? 'badge-failed' : 'badge-warning'}`}>
                        Attempt {item.attemptCount} / 5
                      </span>
                    </td>
                    <td style={{ fontSize: '0.82rem' }}>
                      <span style={{ color: 'var(--red-text)', fontWeight: 700, fontFamily: 'var(--font-mono)' }}>
                        {item.lastErrorCode || 'TRANSIENT_FAULT'}
                      </span>
                      <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '2px', wordBreak: 'break-all', maxWidth: '300px' }}>
                        {item.lastErrorMessage}
                      </div>
                    </td>
                    <td style={{ fontFamily: 'var(--font-mono)', fontSize: '0.82rem', color: 'var(--text-muted)' }}>
                      {item.nextRetryUtc ? new Date(item.nextRetryUtc).toLocaleTimeString() : 'Immediate'}
                    </td>
                    <td>
                      <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
                        <button
                          id={`btn-logs-${item.transferId}`}
                          className="btn btn-secondary btn-sm btn-icon"
                          onClick={() => onSelectTransfer && onSelectTransfer(item)}
                          title="Logs: error messages, audit trail, and file details"
                          aria-label="Logs"
                        >
                          <Icon name="logs" />
                        </button>
                        <button
                          id={`btn-retry-${item.transferId}`}
                          className="btn btn-primary btn-sm btn-icon"
                          onClick={() => handleRetry(item)}
                          disabled={Boolean(retrying[item.transferId])}
                          title={retrying[item.transferId] ? 'Retrying...' : 'Retry this file now'}
                          aria-label={retrying[item.transferId] ? 'Retrying' : 'Retry this file'}
                        >
                          {retrying[item.transferId] ? <span className="btn-spinner" aria-hidden="true" /> : <Icon name="retry" />}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
