import React, { useState, useEffect } from 'react';
import { fetchTransferLogs } from '../fsApi';

const LEVEL_COLORS = {
  ERROR: 'var(--red-text)',
  WARN: '#f59e0b',
  INFO: 'var(--text-secondary)',
  DEBUG: 'var(--text-muted)'
};

// The actual log lines about one transfer's file (errors with their messages, retries, archive moves), oldest first.
export default function TransferLogs({ transferId }) {
  const [entries, setEntries] = useState(null);
  const [error, setError] = useState('');

  useEffect(() => {
    let cancelled = false;
    setEntries(null);
    setError('');
    fetchTransferLogs(transferId)
      .then(data => { if (!cancelled) setEntries(Array.isArray(data) ? data : []); })
      .catch(err => { if (!cancelled) setError(err.message); });
    return () => { cancelled = true; };
  }, [transferId]);

  const formatTime = (iso) => {
    try {
      return `${new Date(iso).toISOString().replace('T', ' ').slice(0, 19)} UTC`;
    } catch {
      return iso;
    }
  };

  return (
    <div style={{ marginBottom: '20px' }} id="transfer-logs">
      <div style={{ fontSize: '0.82rem', fontWeight: 700, color: 'var(--primary-blue)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '8px' }}>
        Logs
      </div>
      <div style={{
        background: 'var(--gray-bg)',
        border: '1px solid var(--card-border)',
        borderRadius: '8px',
        padding: '8px 10px',
        maxHeight: '260px',
        overflowY: 'auto',
        fontFamily: 'var(--font-mono)',
        fontSize: '0.74rem',
        lineHeight: 1.5
      }}>
        {error && <div style={{ color: 'var(--red-text)' }}>{error}</div>}
        {!error && entries === null && <div style={{ color: 'var(--text-muted)' }}>Loading logs...</div>}
        {!error && entries !== null && entries.length === 0 && (
          <div style={{ color: 'var(--text-muted)' }}>No log lines mention this file (older lines may have rotated out of the log).</div>
        )}
        {!error && entries !== null && entries.map((entry, i) => (
          <div key={i} style={{ padding: '3px 0', borderBottom: i < entries.length - 1 ? '1px solid var(--card-border)' : 'none', wordBreak: 'break-word' }}>
            <span style={{ color: 'var(--text-muted)' }}>{formatTime(entry.timestamp)}</span>{' '}
            <span style={{ color: LEVEL_COLORS[entry.level] || 'var(--text-secondary)', fontWeight: 700 }}>{entry.level}</span>{' '}
            <span style={{ color: 'var(--text-main)' }}>{entry.message}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
