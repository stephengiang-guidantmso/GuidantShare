import React, { useState } from 'react';
import { remoteShortName } from '../endpointTypes';
import Icon from './Icon';

// "SFTP sftp.partner.com:/outgoing", "Aetna /StarsMedicare_Reports/fromAetna", "ShareFile Clients/Partner"
const remoteLabel = (wf) => {
  const path = wf.sftpRemotePath || '/';
  return wf.endpointType && wf.endpointType !== 'Sftp'
    ? `${remoteShortName(wf.endpointType)} ${path}`
    : `SFTP ${wf.sftpConfig?.host ? `${wf.sftpConfig.host}:` : ''}${path}`;
};
const shareFileLabel = (wf) => `ShareFile ${wf.shareFileDisplayPath || wf.shareFileFolderId || ''}`.trim();

const ORDER_KEY = 'jobOrder';

const loadOrder = () => {
  try {
    const saved = JSON.parse(localStorage.getItem(ORDER_KEY));
    return Array.isArray(saved) ? saved : [];
  } catch {
    return [];
  }
};

// Saved order first; jobs not in it (newly created) keep their server order at the end
const applyOrder = (workflows, order) => {
  const rank = new Map(order.map((id, i) => [id, i]));
  return workflows
    .map((wf, i) => ({ wf, i }))
    .sort((a, b) => (rank.get(a.wf.workflowId) ?? order.length + a.i) - (rank.get(b.wf.workflowId) ?? order.length + b.i))
    .map(x => x.wf);
};

export default function WorkflowList({
  workflows, 
  onTriggerRun, 
  onToggleWorkflow, 
  onRefresh, 
  onOpenObserve, 
  onEditJob, 
  onOpenNewJob,
  onRunAll,
  onCancelRun,
  onShowTransfers
}) {
  const [runningWf, setRunningWf] = useState(null);
  const [runningAll, setRunningAll] = useState(false);
  const [cancellingWf, setCancellingWf] = useState(null);
  const [order, setOrder] = useState(loadOrder);
  const [dragId, setDragId] = useState(null);
  const [overId, setOverId] = useState(null);

  const orderedWorkflows = applyOrder(workflows, order);

  const handleDrop = (targetId) => {
    if (dragId && dragId !== targetId) {
      const ids = orderedWorkflows.map(w => w.workflowId);
      ids.splice(ids.indexOf(dragId), 1);
      ids.splice(ids.indexOf(targetId), 0, dragId);
      setOrder(ids);
      try {
        localStorage.setItem(ORDER_KEY, JSON.stringify(ids));
      } catch { /* order just won't persist */ }
    }
    setDragId(null);
    setOverId(null);
  };

  const handleCancel = async (wfId) => {
    if (!onCancelRun) return;
    setCancellingWf(wfId);
    try {
      await onCancelRun(wfId);
    } finally {
      setCancellingWf(null);
    }
  };

  const handleRun = async (wfId, mode) => {
    setRunningWf(`${wfId}-${mode}`);
    try {
      await onTriggerRun(wfId, mode);
    } finally {
      setRunningWf(null);
    }
  };

  const handleRunAllClick = async () => {
    if (!onRunAll) return;
    setRunningAll(true);
    try {
      await onRunAll();
    } finally {
      setRunningAll(false);
    }
  };

  const formatLastRun = (isoString) => {
    if (!isoString) return null;
    try {
      const d = new Date(isoString);
      const time = d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
      const date = d.toLocaleDateString([], { month: 'short', day: 'numeric' });
      return `${time} (${date})`;
    } catch {
      return isoString;
    }
  };

  return (
    <div id="workflows-section" style={{ marginBottom: '28px' }}>
      <div className="section-header">
        <div className="section-title">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <polyline points="23 4 23 10 17 10"></polyline>
            <polyline points="1 20 1 14 7 14"></polyline>
            <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>
          </svg>
          <span>CONFIGURED TRANSFER JOBS ({workflows.length})</span>
        </div>
        
        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
          <button
            id="btn-run-all-jobs"
            className="btn btn-secondary btn-sm btn-icon"
            onClick={handleRunAllClick}
            disabled={runningAll}
            title={runningAll ? 'Running all enabled jobs...' : 'Run every enabled job now'}
            aria-label={runningAll ? 'Running all jobs' : 'Run All Jobs'}
            style={{ width: '36px' }}
          >
            {runningAll ? <span className="btn-spinner" aria-hidden="true" /> : <Icon name="playAll" />}
          </button>
          <button
            id="btn-add-new-job"
            className="btn btn-primary btn-sm btn-icon"
            onClick={onOpenNewJob}
            title="New job"
            aria-label="New job"
            style={{ width: '36px' }}
          >
            <Icon name="plus" />
          </button>
        </div>
      </div>

      <div className="content-card">
        <div className="table-container">
          <table id="workflows-table">
            <thead>
              <tr>
                <th style={{ width: '28px' }}></th>
                <th style={{ width: '420px' }}>Transfer Job</th>
                <th style={{ whiteSpace: 'nowrap' }}>Direction</th>
                <th style={{ whiteSpace: 'nowrap' }}>Last Delivery</th>
                <th style={{ whiteSpace: 'nowrap' }}>Execution State</th>
                <th style={{ whiteSpace: 'nowrap' }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {orderedWorkflows.map((wf) => {
                // Backend reports runs started by the scheduler too; local state covers the gap before the next poll
                const isRunning = Boolean(wf.isRunning) || runningWf === `${wf.workflowId}-Live`;
                return (
                <tr
                  key={wf.workflowId}
                  id={`workflow-row-${wf.workflowId}`}
                  onDragOver={(e) => { if (dragId) { e.preventDefault(); setOverId(wf.workflowId); } }}
                  onDrop={(e) => { e.preventDefault(); handleDrop(wf.workflowId); }}
                  style={{
                    opacity: dragId === wf.workflowId ? 0.45 : 1,
                    boxShadow: dragId && overId === wf.workflowId && dragId !== wf.workflowId ? 'inset 0 2px 0 var(--primary-blue)' : 'none'
                  }}
                >
                  {/* Drag handle: only the grip starts a drag, so text and buttons stay selectable */}
                  <td style={{ width: '28px', paddingRight: 0 }}>
                    <span
                      draggable
                      onDragStart={(e) => { e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', wf.workflowId); setDragId(wf.workflowId); }}
                      onDragEnd={() => { setDragId(null); setOverId(null); }}
                      title="Drag to reorder"
                      aria-label={`Reorder ${wf.name}`}
                      style={{ cursor: 'grab', color: 'var(--text-muted)', display: 'inline-flex' }}
                    >
                      <Icon name="grip" />
                    </span>
                  </td>
                  {/* Title & Remote Path (Clickable to Edit); fixed width so the action buttons never run off the right */}
                  <td style={{ width: '420px', maxWidth: '420px' }}>
                    <div 
                      onClick={() => onEditJob && onEditJob(wf)}
                      title="Click to edit Transfer Job settings and SFTP credentials"
                      style={{ 
                        fontWeight: 700, 
                        color: 'var(--primary-blue)', 
                        cursor: 'pointer',
                        display: 'inline-flex',
                        alignItems: 'center'
                      }}
                    >
                      <span style={{ textDecoration: 'underline', textUnderlineOffset: '3px' }}>{wf.name}</span>
                    </div>
                    <div style={{ fontSize: '0.78rem', color: 'var(--text-muted)', fontFamily: 'var(--font-mono)', marginTop: '2px', lineHeight: 1.45 }}>
                      {[
                        ['Source', wf.direction === 'ShareFileToSftp' ? shareFileLabel(wf) : remoteLabel(wf)],
                        ['Target', wf.direction === 'ShareFileToSftp' ? remoteLabel(wf) : shareFileLabel(wf)]
                      ].map(([label, value]) => (
                        <div key={label} title={value} style={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                          {label}: {value}
                        </div>
                      ))}
                    </div>
                  </td>

                  {/* Direction Badge */}
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <span className={`status-badge ${wf.direction === 'SftpToShareFile' ? 'badge-direction-sftp-sharefile' : 'badge-direction-sharefile-sftp'}`}>
                      {wf.direction === 'SftpToShareFile' ? `${remoteShortName(wf.endpointType)} → ShareFile` : `ShareFile → ${remoteShortName(wf.endpointType)}`}
                    </span>
                  </td>

                  {/* Last Delivery (Timestamp with Files Transferred count subtext) */}
                  <td style={{ whiteSpace: 'nowrap' }}>
                    {wf.lastSuccessfulRunUtc && wf.lastTransferredCount > 0 ? (
                      <div>
                        <div style={{ fontSize: '0.82rem', fontWeight: 600, color: 'var(--text-main)' }}>
                          {formatLastRun(wf.lastSuccessfulRunUtc)}
                        </div>
                        <a
                          href="#ledger"
                          onClick={(e) => { e.preventDefault(); onShowTransfers && onShowTransfers(wf.workflowId); }}
                          title="Open this job's transfers in the Transfer Ledger"
                          style={{ fontSize: '0.74rem', color: 'var(--green-text)', fontFamily: 'var(--font-mono)', textDecoration: 'underline', textUnderlineOffset: '2px' }}
                        >
                          {`${wf.lastTransferredCount} file${wf.lastTransferredCount > 1 ? 's' : ''} transferred`}
                        </a>
                      </div>
                    ) : wf.lastRunUtc ? (
                      <div>
                        <div style={{ fontSize: '0.82rem', fontWeight: 600, color: 'var(--text-main)' }}>
                          {formatLastRun(wf.lastRunUtc)}
                        </div>
                        <div style={{ fontSize: '0.74rem', color: 'var(--text-muted)', fontFamily: 'var(--font-mono)' }}>
                          No files transferred yet
                        </div>
                      </div>
                    ) : (
                      <div style={{ fontSize: '0.78rem', color: 'var(--text-muted)', fontStyle: 'italic' }}>
                        No deliveries yet
                      </div>
                    )}
                  </td>

                  {/* Execution State (Active, Disabled, or Failed) */}
                  <td style={{ whiteSpace: 'nowrap' }}>
                    {isRunning ? (
                      <span className="status-badge badge-warning">
                        Running
                      </span>
                    ) : !wf.isEnabled ? (
                      <span className="status-badge badge-normal">
                        Disabled
                      </span>
                    ) : wf.lastStatus === 'Failed' ? (
                      <span className="status-badge badge-failed">
                        Failed
                      </span>
                    ) : (
                      <span className="status-badge badge-nominal">
                        Active
                      </span>
                    )}
                  </td>

                  {/* Operational Controls */}
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <div style={{ display: 'flex', gap: '8px', flexWrap: 'nowrap', alignItems: 'center' }}>
                      <button
                        id={`btn-run-live-${wf.workflowId}`}
                        className="btn btn-primary btn-sm btn-icon"
                        disabled={isRunning || runningWf !== null}
                        onClick={() => handleRun(wf.workflowId, 'Live')}
                        title={isRunning ? 'Running...' : 'Run now'}
                        aria-label={isRunning ? 'Running' : 'Run now'}
                      >
                        {isRunning ? <span className="btn-spinner" aria-hidden="true" /> : <Icon name="play" />}
                      </button>
                      {/* Same spot: Dry run while idle, Cancel while the job runs */}
                      {isRunning ? (
                        <button
                          id={`btn-cancel-${wf.workflowId}`}
                          className="btn btn-sm btn-icon btn-cancel-run"
                          disabled={cancellingWf === wf.workflowId}
                          onClick={() => handleCancel(wf.workflowId)}
                          title={cancellingWf === wf.workflowId ? 'Cancelling...' : 'Cancel the running job'}
                          aria-label="Cancel the running job"
                        >
                          {cancellingWf === wf.workflowId ? <span className="btn-spinner" aria-hidden="true" /> : <Icon name="stop" />}
                        </button>
                      ) : (
                        <button
                          id={`btn-observe-${wf.workflowId}`}
                          className="btn btn-secondary btn-sm btn-icon"
                          onClick={() => onOpenObserve && onOpenObserve(wf)}
                          title="Dry run: preview what would transfer, without moving anything"
                          aria-label="Dry run"
                        >
                          <Icon name="eye" />
                        </button>
                      )}
                      <button
                        id={`btn-toggle-${wf.workflowId}`}
                        className="btn btn-secondary btn-sm btn-icon"
                        onClick={() => onToggleWorkflow(wf.workflowId)}
                        title={wf.isEnabled ? 'Pause schedule' : 'Resume schedule'}
                        aria-label={wf.isEnabled ? 'Pause schedule' : 'Resume schedule'}
                      >
                        <Icon name={wf.isEnabled ? 'pause' : 'resume'} />
                      </button>
                    </div>
                  </td>
                </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
