import React, { useState } from 'react';
import { triggerWorkflowRun } from '../fsApi';
import { overlayCloseProps } from './overlayClose';

export default function ObservationModal({ workflow, onClose, onRefreshData }) {
  if (!workflow) return null;

  const [runResult, setRunResult] = useState(null);
  const [isLoading, setIsLoading] = useState(false);
  const [isLiveRunning, setIsLiveRunning] = useState(false);
  const [liveSuccessMsg, setLiveSuccessMsg] = useState(null);
  const [runCount, setRunCount] = useState(0);
  const [actionFilter, setActionFilter] = useState('all');

  // Planned actions grouped for the filter tabs
  const actionCategory = (action = '') =>
    action.startsWith('Eligible') ? 'transfer' : action.startsWith('Delete') ? 'delete' : 'skip';
  const observed = runResult?.observedFiles || [];
  const categoryCounts = observed.reduce((counts, f) => {
    counts[actionCategory(f.action)] += 1;
    return counts;
  }, { transfer: 0, delete: 0, skip: 0 });
  const visibleFiles = actionFilter === 'all' ? observed : observed.filter(f => actionCategory(f.action) === actionFilter);

  const formatBytes = (bytes) => {
    if (!bytes || bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  };

  const handleExecuteDryRun = async () => {
    setIsLoading(true);
    setLiveSuccessMsg(null);
    try {
      const res = await triggerWorkflowRun(workflow.workflowId, 'Observation');
      setRunResult(res);
      setRunCount(prev => prev + 1);
      if (onRefreshData) onRefreshData();
    } catch (err) {
      console.error('Failed to execute observation dry-run:', err);
    } finally {
      setIsLoading(false);
    }
  };

  const handleExecuteLiveRun = async () => {
    setIsLiveRunning(true);
    setLiveSuccessMsg(null);
    try {
      const res = await triggerWorkflowRun(workflow.workflowId, 'Live');
      setLiveSuccessMsg(`Live Sync Initiated: ${res.filesTransferred || 0} file(s) transferred successfully.`);
      if (onRefreshData) onRefreshData();
    } catch (err) {
      console.error('Failed to execute live run:', err);
    } finally {
      setIsLiveRunning(false);
    }
  };

  return (
    <div className="modal-overlay" id="observation-modal-overlay" {...overlayCloseProps(onClose)}>
      <div 
        className="modal-content" 
        id="observation-modal-content" 
        onClick={(e) => e.stopPropagation()}
        style={{ maxWidth: '840px', width: '92%' }}
      >
        {/* Header */}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px', borderBottom: '1px solid var(--card-border)', paddingBottom: '12px' }}>
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <span className="status-badge badge-nominal" style={{ background: '#e0f2fe', color: '#0369a1', borderColor: '#bae6fd' }}>
                DRY-RUN INSPECTION
              </span>
              <h3 style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--text-main)', margin: 0 }}>
                {workflow.name}
              </h3>
            </div>
            <div style={{ fontSize: '0.8rem', color: 'var(--text-muted)', marginTop: '4px' }}>
              Job ID: <span style={{ fontFamily: 'var(--font-mono)' }}>{workflow.workflowId}</span>
              {runCount > 0 && runResult && (
                <span> • Dry-Runs Executed: <strong>{runCount} time{runCount > 1 ? 's' : ''}</strong> (Last at {new Date(runResult.completedUtc || runResult.startedUtc || Date.now()).toLocaleTimeString()})</span>
              )}
            </div>
          </div>
          <button 
            id="btn-close-observation-modal"
            className="btn btn-secondary btn-sm" 
            onClick={onClose}
          >
            Close
          </button>
        </div>

        {/* Safety Banner */}
        <div style={{ 
          background: 'rgba(59, 130, 246, 0.08)', 
          border: '1px solid rgba(59, 130, 246, 0.25)', 
          borderRadius: '8px', 
          padding: '10px 14px', 
          marginBottom: '18px',
          display: 'flex',
          alignItems: 'center',
          gap: '10px',
          fontSize: '0.85rem',
          color: 'var(--text-main)'
        }}>
          <div>
            <strong>100% Non-Destructive Simulation:</strong> Dry-run inspection only scans remote directories and validates file stability. No files will be moved, downloaded, or altered on either endpoint.
          </div>
        </div>

        {/* Remote Configuration Paths */}
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '14px', marginBottom: '18px' }}>
          <div style={{ background: 'var(--gray-bg)', padding: '10px 14px', borderRadius: '8px', border: '1px solid var(--card-border)' }}>
            <div style={{ fontSize: '0.72rem', color: 'var(--text-muted)', textTransform: 'uppercase', fontWeight: 700 }}>
              {workflow.direction === 'ShareFileToSftp' ? 'Source Path (ShareFile)' : 'Source Path (SFTP)'}
            </div>
            <div style={{ fontFamily: 'var(--font-mono)', fontSize: '0.84rem', color: 'var(--text-main)', marginTop: '2px', wordBreak: 'break-all' }}>
              {workflow.direction === 'ShareFileToSftp'
                ? (workflow.shareFileDisplayPath || workflow.shareFileFolderId || 'ShareFile Folder')
                : (workflow.sftpRemotePath || 'Remote SFTP Directory')}
            </div>
          </div>
          <div style={{ background: 'var(--gray-bg)', padding: '10px 14px', borderRadius: '8px', border: '1px solid var(--card-border)' }}>
            <div style={{ fontSize: '0.72rem', color: 'var(--text-muted)', textTransform: 'uppercase', fontWeight: 700 }}>
              {workflow.direction === 'ShareFileToSftp' ? 'Destination Path (SFTP)' : 'Destination Path (ShareFile)'}
            </div>
            <div style={{ fontFamily: 'var(--font-mono)', fontSize: '0.84rem', color: 'var(--text-main)', marginTop: '2px', wordBreak: 'break-all' }}>
              {workflow.direction === 'ShareFileToSftp'
                ? (workflow.sftpRemotePath || 'Remote SFTP Directory')
                : (workflow.shareFileDisplayPath || workflow.shareFileFolderId || 'ShareFile Folder')}
            </div>
          </div>
        </div>

        {/* Dynamic Dry-Run State: Initial vs Populated */}
        {!runResult ? (
          <div style={{ 
            background: 'var(--gray-bg)', 
            border: '2px dashed var(--card-border)', 
            borderRadius: '10px', 
            padding: '36px 20px', 
            textAlign: 'center',
            marginBottom: '20px'
          }}>
            <h4 style={{ margin: '0 0 6px 0', fontSize: '1.05rem', color: 'var(--text-main)' }}>
              Ready to Inspect Transfer Job
            </h4>
            <p style={{ margin: 0, fontSize: '0.85rem', color: 'var(--text-muted)', maxWidth: '460px', marginLeft: 'auto', marginRight: 'auto' }}>
              Click <strong>"Run Dry-Run Simulation"</strong> below to scan remote endpoints, evaluate file stability, and verify which files are ready for transfer.
            </p>
          </div>
        ) : (
          <>
            {/* Summary Telemetry Metrics */}
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: '12px', marginBottom: '18px' }}>
              <div style={{ background: 'var(--gray-bg)', padding: '10px 14px', borderRadius: '8px', border: '1px solid var(--card-border)' }}>
                <div style={{ fontSize: '0.72rem', color: 'var(--text-muted)', textTransform: 'uppercase', fontWeight: 700 }}>Scanned Files</div>
                <div style={{ fontSize: '1.3rem', fontWeight: 800, color: 'var(--primary-blue)', fontFamily: 'var(--font-mono)' }}>{runResult.filesDiscovered || 0}</div>
              </div>
              <div style={{ background: 'var(--gray-bg)', padding: '10px 14px', borderRadius: '8px', border: '1px solid var(--card-border)' }}>
                <div style={{ fontSize: '0.72rem', color: 'var(--text-muted)', textTransform: 'uppercase', fontWeight: 700 }}>Eligible to Transfer</div>
                <div style={{ fontSize: '1.3rem', fontWeight: 800, color: 'var(--green-text)', fontFamily: 'var(--font-mono)' }}>{runResult.filesObserved || 0}</div>
              </div>
              <div style={{ background: 'var(--gray-bg)', padding: '10px 14px', borderRadius: '8px', border: '1px solid var(--card-border)' }}>
                <div style={{ fontSize: '0.72rem', color: 'var(--text-muted)', textTransform: 'uppercase', fontWeight: 700 }}>Filtered / Skipped</div>
                <div style={{ fontSize: '1.3rem', fontWeight: 800, color: '#f59e0b', fontFamily: 'var(--font-mono)' }}>{runResult.filesExcluded || 0}</div>
              </div>
              <div style={{ background: 'var(--gray-bg)', padding: '10px 14px', borderRadius: '8px', border: '1px solid var(--card-border)' }}>
                <div style={{ fontSize: '0.72rem', color: 'var(--text-muted)', textTransform: 'uppercase', fontWeight: 700 }}>Data Transferred</div>
                <div style={{ fontSize: '1.3rem', fontWeight: 800, color: 'var(--text-main)', fontFamily: 'var(--font-mono)' }}>0 Bytes</div>
              </div>
            </div>

            {/* Problems found while listing (e.g. folders FileSync could not open); their files are not in the list below */}
            {runResult.errorSummary && (
              <div style={{ background: 'var(--red-bg)', border: '1px solid var(--red-border)', color: 'var(--red-text)', padding: '10px 14px', borderRadius: '8px', marginBottom: '16px', fontSize: '0.82rem' }}>
                <strong>{runResult.status === 'Failed' ? 'The dry run failed (nothing could be listed):' : 'Warnings:'}</strong>
                {runResult.errorSummary.split(' | ').map((msg, i) => (
                  <div key={i} style={{ marginTop: '4px', wordBreak: 'break-word' }}>{msg}</div>
                ))}
              </div>
            )}

            {/* Discovered Files Table */}
            <div style={{ marginBottom: '20px' }}>
              <div style={{ fontSize: '0.78rem', fontWeight: 700, textTransform: 'uppercase', color: 'var(--text-secondary)', marginBottom: '8px' }}>
                {workflow.direction === 'ShareFileToSftp' ? 'Discovered ShareFile Files & Decision Matrix' : 'Discovered Remote SFTP Files & Decision Matrix'}
              </div>
              <div className="verify-filter-tabs" style={{ marginBottom: '8px' }}>
                {[
                  ['all', `All (${observed.length})`],
                  ['transfer', `To Transfer (${categoryCounts.transfer})`],
                  ['delete', `To Delete (${categoryCounts.delete})`],
                  ['skip', `Skipped (${categoryCounts.skip})`]
                ].map(([value, label]) => (
                  <button
                    key={value}
                    type="button"
                    className={`verify-filter-tab ${actionFilter === value ? 'active' : ''}`}
                    onClick={() => setActionFilter(value)}
                  >
                    {label}
                  </button>
                ))}
              </div>
              <div className="table-container" style={{ maxHeight: '220px', overflowY: 'auto', border: '1px solid var(--card-border)', borderRadius: '8px' }}>
                <table>
                  <thead>
                    <tr>
                      <th>Filename</th>
                      <th>Size</th>
                      <th>Stability Verification</th>
                      <th>Planned Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {visibleFiles.length > 0 ? (
                      visibleFiles.map((f, i) => (
                        <tr key={i}>
                          <td>
                            <div style={{ fontWeight: 700, fontFamily: 'var(--font-mono)', fontSize: '0.85rem', color: 'var(--text-main)' }}>
                              {f.filename}
                            </div>
                            {f.destination !== 'N/A' && (
                              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '2px' }}>
                                → {f.destination}
                              </div>
                            )}
                          </td>
                          <td style={{ fontFamily: 'var(--font-mono)', fontSize: '0.82rem' }}>
                            {formatBytes(f.sizeBytes)}
                          </td>
                          <td>
                            <span style={{ 
                              fontSize: '0.78rem', 
                              color: f.action.includes('Eligible') ? 'var(--green-text)' : '#f59e0b',
                              fontWeight: 600
                            }}>
                              {f.stabilityStatus}
                            </span>
                          </td>
                          <td>
                            <span className={`status-badge ${actionCategory(f.action) === 'transfer' ? 'badge-nominal' : actionCategory(f.action) === 'delete' ? 'badge-failed' : 'badge-warning'}`}>
                              {f.action}
                            </span>
                          </td>
                        </tr>
                      ))
                    ) : (
                      <tr>
                        <td colSpan="4" style={{ textAlign: 'center', padding: '16px', color: 'var(--text-muted)' }}>
                          {observed.length > 0
                            ? 'No files in this category.'
                            : workflow.direction === 'ShareFileToSftp'
                            ? 'No files discovered in ShareFile source folder during this polling cycle.' 
                            : 'No files discovered in remote SFTP folder during this polling cycle.'}
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          </>
        )}

        {liveSuccessMsg && (
          <div style={{ background: 'rgba(34, 197, 94, 0.12)', border: '1px solid rgba(34, 197, 94, 0.3)', color: 'var(--green-text)', padding: '10px 14px', borderRadius: '8px', marginBottom: '16px', fontSize: '0.85rem', display: 'flex', alignItems: 'center', gap: '8px' }}>
            <span>{liveSuccessMsg}</span>
          </div>
        )}

        {/* Modal Footer Controls */}
        <div style={{ borderTop: '1px solid var(--card-border)', paddingTop: '16px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <button 
            id="btn-close-dry-run-modal"
            className="btn btn-secondary" 
            onClick={onClose}
            disabled={isLoading || isLiveRunning}
          >
            Close
          </button>

          <div style={{ display: 'flex', gap: '10px', alignItems: 'center' }}>
            {runResult && (
              <button
                type="button"
                id="btn-rerun-dry-run-secondary"
                className="btn btn-secondary"
                disabled={isLoading || isLiveRunning}
                onClick={handleExecuteDryRun}
                title="Re-run non-destructive directory inspection"
              >
                {isLoading ? 'Scanning...' : 'Re-Run Dry Run'}
              </button>
            )}

            {!runResult ? (
              <button 
                id="btn-run-dry-run-inside-modal"
                className="btn btn-primary"
                disabled={isLoading}
                onClick={handleExecuteDryRun}
              >
                {isLoading ? 'Scanning Remote Folders...' : 'Run Dry-Run Simulation'}
              </button>
            ) : (
              <button 
                id="btn-run-live-job-inside-modal"
                className="btn btn-primary"
                disabled={isLiveRunning || isLoading}
                onClick={handleExecuteLiveRun}
                style={{ background: '#10b981', borderColor: '#059669', color: '#ffffff', fontWeight: 700 }}
                title="Execute immediate live transfer of eligible files"
              >
                {isLiveRunning ? 'Executing Live Sync...' : 'Run Job Now'}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
