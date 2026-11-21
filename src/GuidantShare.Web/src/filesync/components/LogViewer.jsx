import React, { useState } from 'react';

export default function LogViewer({ logs, onRefresh }) {
  const [search, setSearch] = useState('');
  const [levelFilter, setLevelFilter] = useState('ALL');

  const filteredLogs = logs.filter(log => {
    if (levelFilter !== 'ALL' && log.level !== levelFilter) return false;
    if (search && !log.message.toLowerCase().includes(search.toLowerCase())) return false;
    return true;
  });

  return (
    <div id="logs-section" style={{ marginBottom: '28px' }}>
      <div className="section-header">
        <div className="section-title">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <polyline points="4 17 10 11 4 5"></polyline>
            <line x1="12" y1="19" x2="20" y2="19"></line>
          </svg>
          <span>REAL-TIME AUDIT & OPERATIONAL TELEMETRY LOGS</span>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
          <input
            id="log-search-input"
            type="text"
            placeholder="Search log entries..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            style={{
              background: 'var(--card-bg)',
              border: '1px solid var(--card-border)',
              color: 'var(--text-main)',
              padding: '6px 12px',
              borderRadius: '8px',
              fontSize: '0.82rem',
              outline: 'none',
              width: '180px'
            }}
          />
          <select
            id="log-level-select"
            value={levelFilter}
            onChange={(e) => setLevelFilter(e.target.value)}
            style={{
              background: 'var(--card-bg)',
              border: '1px solid var(--card-border)',
              color: 'var(--text-main)',
              padding: '6px 10px',
              borderRadius: '8px',
              fontSize: '0.82rem',
              outline: 'none'
            }}
          >
            <option value="ALL">All Levels</option>
            <option value="INFO">INFO</option>
            <option value="WARN">WARN</option>
            <option value="ERROR">ERROR</option>
            <option value="DEBUG">DEBUG</option>
          </select>
        </div>
      </div>

      <div className="terminal-panel" id="log-terminal-output">
        {filteredLogs.map(log => (
          <div key={log.id} className="terminal-entry">
            <span className="terminal-time">[{new Date(log.timestamp).toLocaleTimeString()}]</span>
            <span className={`terminal-level terminal-level-${log.level.toLowerCase()}`}>
              {log.level}
            </span>
            <span className="terminal-msg">
              {log.workflowId && <strong style={{ color: 'var(--primary-blue)' }}>[{log.workflowId}] </strong>}
              {log.message}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}
