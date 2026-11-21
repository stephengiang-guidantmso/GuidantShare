import React, { useState, useEffect, useRef } from 'react';
import { startVerification, fetchVerification, fetchLatestVerification, verificationCsvUrl } from '../fsApi';
import Icon from './Icon';

const SELECTED_JOB_KEY = 'verifySync.workflowId';
const MAX_ROWS = 500;

const STATUS_META = {
  MissingInShareFile: { label: 'Missing in ShareFile', hint: 'On SFTP but not in ShareFile', badge: 'badge-critical', tone: 'red' },
  SizeDiffers: { label: 'Size differs', hint: 'Same path, different size', badge: 'badge-warning', tone: 'orange' },
  OnlyInShareFile: { label: 'Only in ShareFile', hint: 'In ShareFile but not on SFTP', badge: 'badge-normal', tone: 'blue' },
  Excluded: { label: 'Excluded', hint: "Skipped by the job's exclude patterns", badge: 'badge-normal', tone: 'muted' },
  Match: { label: 'Match', hint: 'Same path and size on both sides', badge: 'badge-nominal', tone: 'green' }
};
const STATUS_ORDER = ['MissingInShareFile', 'SizeDiffers', 'OnlyInShareFile', 'Excluded', 'Match'];
const DIFFERENCE_STATUSES = ['MissingInShareFile', 'SizeDiffers', 'OnlyInShareFile'];

const formatBytes = (bytes) => {
  if (bytes === null || bytes === undefined) return '-';
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.min(Math.floor(Math.log(bytes) / Math.log(k)), sizes.length - 1);
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
};

const formatDuration = (ms) => {
  const totalSeconds = Math.max(0, Math.round(ms / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return minutes > 0 ? `${minutes}m ${seconds}s` : `${seconds}s`;
};

const formatDateTime = (iso) => {
  if (!iso) return '-';
  const d = new Date(iso);
  return `${d.toLocaleDateString([], { month: 'short', day: 'numeric' })} ${d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;
};

const readSavedJob = () => {
  try { return localStorage.getItem(SELECTED_JOB_KEY) || ''; } catch { return ''; }
};

/**
 * Verify Sync: read-only check that a job's SFTP folder and ShareFile folder hold the same files (path + size).
 * The comparison runs on the server using FileSync's own connections; this page starts it and shows the result.
 */
export default function VerifySync({ workflows = [] }) {
  const [workflowId, setWorkflowId] = useState(readSavedJob);
  const [run, setRun] = useState(null);
  const [error, setError] = useState('');
  const [isStarting, setIsStarting] = useState(false);
  const [filter, setFilter] = useState('differences');
  const [search, setSearch] = useState('');
  const [now, setNow] = useState(Date.now());
  const loadRequest = useRef(0);

  // Fall back to the first job when nothing (or a deleted job) is selected
  useEffect(() => {
    if (workflows.length === 0) return;
    if (!workflows.some(wf => wf.workflowId === workflowId)) {
      setWorkflowId(workflows[0].workflowId);
    }
  }, [workflows, workflowId]);

  // Load the job's last verification when the job changes
  useEffect(() => {
    if (!workflowId) return;
    const thisRequest = ++loadRequest.current;
    setRun(null);
    setError('');
    fetchLatestVerification(workflowId)
      .then(data => { if (thisRequest === loadRequest.current) setRun(data); })
      .catch(err => { if (thisRequest === loadRequest.current) setError(err.message); });
  }, [workflowId]);

  // Poll while a verification is running
  const isRunning = run?.status === 'Running';
  useEffect(() => {
    if (!isRunning) return;
    const verificationId = run.verificationId;
    const timer = setInterval(async () => {
      try {
        const latest = await fetchVerification(verificationId);
        setRun(prev => (prev && prev.verificationId === verificationId ? latest : prev));
      } catch (err) {
        setError(err.message);
      }
      setNow(Date.now());
    }, 2000);
    const clock = setInterval(() => setNow(Date.now()), 1000);
    return () => { clearInterval(timer); clearInterval(clock); };
  }, [isRunning, run?.verificationId]);

  const selectJob = (id) => {
    setWorkflowId(id);
    setFilter('differences');
    setSearch('');
    try { localStorage.setItem(SELECTED_JOB_KEY, id); } catch { /* storage unavailable */ }
  };

  const handleStart = async () => {
    if (!workflowId) return;
    setIsStarting(true);
    setError('');
    try {
      const started = await startVerification(workflowId);
      setRun(started);
      setFilter('differences');
      setSearch('');
      setNow(Date.now());
    } catch (err) {
      setError(err.message);
    } finally {
      setIsStarting(false);
    }
  };

  const selectedJob = workflows.find(wf => wf.workflowId === workflowId);
  const isUpload = selectedJob?.direction === 'ShareFileToSftp';
  const counts = run?.counts || {};
  const differenceCount = DIFFERENCE_STATUSES.reduce((sum, st) => sum + (counts[st] || 0), 0);
  const isCompleted = run?.status === 'Completed';

  const searchLower = search.trim().toLowerCase();
  const filteredItems = (run?.items || []).filter(item => {
    if (filter === 'differences' && !DIFFERENCE_STATUSES.includes(item.status)) return false;
    if (filter !== 'differences' && filter !== 'all' && item.status !== filter) return false;
    if (searchLower && !item.path.toLowerCase().includes(searchLower)) return false;
    return true;
  });
  const visibleItems = filteredItems.slice(0, MAX_ROWS);

  const blockedFolders = [
    ...(run?.sftpBlockedFolders || []).map(b => ({ ...b, side: 'SFTP' })),
    ...(run?.shareFileBlockedFolders || []).map(b => ({ ...b, side: 'ShareFile' }))
  ];

  return (
    <div id="verify-section" style={{ marginBottom: '28px' }}>
      <div className="section-header">
        <div className="section-title">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path>
            <polyline points="9 12 11 14 15 10"></polyline>
          </svg>
          <span>VERIFY SYNC (READ-ONLY)</span>
        </div>
      </div>

      <div className="ledger-filter-bar">
        <label className="ledger-filter-field">
          <span>Job</span>
          <select id="verify-job" value={workflowId} onChange={e => selectJob(e.target.value)} disabled={isRunning}>
            {workflows.length === 0 && <option value="">No jobs configured</option>}
            {workflows.map(wf => (
              <option key={wf.workflowId} value={wf.workflowId}>
                {wf.name} ({wf.direction === 'ShareFileToSftp' ? 'ShareFile to SFTP' : 'SFTP to ShareFile'})
              </option>
            ))}
          </select>
        </label>

        {selectedJob && (
          <div className="verify-route">
            <div><span className="verify-route-label">SFTP</span>{selectedJob.sftpRemotePath || '/'}</div>
            <div><span className="verify-route-label">ShareFile</span>{selectedJob.shareFileDisplayPath || selectedJob.shareFileFolderId}</div>
          </div>
        )}

        <div className="ledger-filter-summary">
          <button
            id="btn-run-verification"
            className="btn btn-primary btn-sm btn-icon"
            onClick={handleStart}
            disabled={!workflowId || isRunning || isStarting}
            title={isRunning || isStarting ? 'Verifying...' : 'Run verification: compare every file path and size on both sides. Nothing is changed.'}
            aria-label={isRunning || isStarting ? 'Verifying' : 'Run verification'}
            style={{ width: '36px' }}
          >
            {isRunning || isStarting ? <span className="btn-spinner" aria-hidden="true" /> : <Icon name="verify" />}
          </button>
        </div>
      </div>

      {error && <div className="content-card explorer-message explorer-message-error" style={{ marginBottom: '14px' }}>{error}</div>}

      {/* Never verified */}
      {!run && !error && (
        <div className="content-card verify-empty">
          <div className="verify-empty-title">This job hasn't been verified yet</div>
          <div>Click the <strong>Run verification</strong> button (the check mark) to compare every file path and size in its SFTP folder and ShareFile folder. Nothing is changed on either side.</div>
        </div>
      )}

      {/* Running */}
      {isRunning && (
        <div className="content-card verify-progress">
          <div className="verify-spinner" aria-hidden="true"></div>
          <div>
            <div className="verify-progress-phase">{run.phase}...</div>
            <div className="verify-progress-meta">
              Running for {formatDuration(now - new Date(run.startedUtc).getTime())}. Large jobs can take several minutes; you can leave this page and come back.
            </div>
          </div>
        </div>
      )}

      {/* Failed */}
      {run?.status === 'Failed' && (
        <div className="verify-banner verify-banner-red">
          <div className="verify-banner-title">Verification failed</div>
          <div>{run.error}</div>
        </div>
      )}

      {/* Completed */}
      {isCompleted && (
        <>
          <div className={`verify-banner ${differenceCount === 0 ? 'verify-banner-green' : 'verify-banner-orange'}`}>
            <div className="verify-banner-title">
              {differenceCount === 0
                ? 'All files match'
                : `${differenceCount.toLocaleString()} difference${differenceCount === 1 ? '' : 's'} found`}
            </div>
            <div>
              Checked {formatDateTime(run.startedUtc)} in {formatDuration(new Date(run.completedUtc) - new Date(run.startedUtc))}
              {' '}&middot; {(counts.Match || 0).toLocaleString()} matching file{counts.Match === 1 ? '' : 's'}
            </div>
          </div>

          <div className="verify-totals">
            <div className="verify-total">
              <div className="verify-total-label">{isUpload ? 'SFTP (destination)' : 'SFTP (source)'}</div>
              <div className="verify-total-value">{run.sftpFiles.toLocaleString()} files</div>
              <div className="verify-total-meta">{formatBytes(run.sftpBytes)} &middot; {run.sftpLocation}</div>
            </div>
            <div className="verify-total">
              <div className="verify-total-label">{isUpload ? 'ShareFile (source)' : 'ShareFile (destination)'}</div>
              <div className="verify-total-value">{run.shareFileFiles.toLocaleString()} files</div>
              <div className="verify-total-meta">{formatBytes(run.shareFileBytes)} &middot; {run.shareFileLocation}</div>
            </div>
          </div>

          <div className="verify-tiles">
            {STATUS_ORDER.map(st => (
              <button
                key={st}
                className={`verify-tile verify-tone-${STATUS_META[st].tone} ${filter === st ? 'verify-tile-active' : ''}`}
                onClick={() => setFilter(filter === st ? 'differences' : st)}
                title={`Show only: ${STATUS_META[st].label}`}
              >
                <div className="verify-tile-count">{(counts[st] || 0).toLocaleString()}</div>
                <div className="verify-tile-label">{STATUS_META[st].label}</div>
                <div className="verify-tile-hint">{STATUS_META[st].hint}</div>
              </button>
            ))}
          </div>

          {(run.shareFileDuplicates?.length > 0 || blockedFolders.length > 0) && (
            <div className="verify-notices">
              {run.shareFileDuplicates?.length > 0 && (
                <details className="verify-notice">
                  <summary>
                    {run.shareFileDuplicates.length.toLocaleString()} path{run.shareFileDuplicates.length === 1 ? '' : 's'} appear more than once in ShareFile (e.g. same-named folders)
                  </summary>
                  <ul>
                    {run.shareFileDuplicates.slice(0, 50).map(d => (
                      <li key={d.path}><span className="verify-mono">{d.count}x</span> {d.path}</li>
                    ))}
                  </ul>
                </details>
              )}
              {blockedFolders.length > 0 && (
                <details className="verify-notice">
                  <summary>{blockedFolders.length} folder{blockedFolders.length === 1 ? '' : 's'} could not be read, so their files were not compared</summary>
                  <ul>
                    {blockedFolders.map(b => (
                      <li key={`${b.side}-${b.relativePath}`}><strong>{b.side}:</strong> {b.relativePath || '(top folder)'} &middot; {b.reason}</li>
                    ))}
                  </ul>
                </details>
              )}
            </div>
          )}

          <div className="content-card">
            <div className="explorer-toolbar">
              <div className="verify-filter-tabs">
                <button className={`verify-filter-tab ${filter === 'differences' ? 'active' : ''}`} onClick={() => setFilter('differences')}>
                  Differences ({differenceCount.toLocaleString()})
                </button>
                <button className={`verify-filter-tab ${filter === 'all' ? 'active' : ''}`} onClick={() => setFilter('all')}>
                  All files ({(run.items?.length || 0).toLocaleString()})
                </button>
                {filter !== 'differences' && filter !== 'all' && (
                  <span className="verify-filter-tab active">{STATUS_META[filter]?.label}</span>
                )}
              </div>
              <input
                className="verify-search"
                type="text"
                placeholder="Search paths..."
                value={search}
                onChange={e => setSearch(e.target.value)}
              />
              <a className="btn btn-secondary btn-sm" href={verificationCsvUrl(run.verificationId)} download>
                Download CSV
              </a>
            </div>

            <div className="table-container">
              <table id="verify-table">
                <thead>
                  <tr>
                    <th>Status</th>
                    <th>Path</th>
                    <th style={{ whiteSpace: 'nowrap' }}>SFTP Size</th>
                    <th style={{ whiteSpace: 'nowrap' }}>ShareFile Size</th>
                  </tr>
                </thead>
                <tbody>
                  {visibleItems.map(item => (
                    <tr key={`${item.status}-${item.path}`}>
                      <td style={{ whiteSpace: 'nowrap' }}>
                        <span className={`status-badge ${STATUS_META[item.status]?.badge || 'badge-normal'}`}>
                          {STATUS_META[item.status]?.label || item.status}
                        </span>
                      </td>
                      <td className="verify-mono verify-path">{item.path}</td>
                      <td className="verify-mono verify-size">{formatBytes(item.sftpSize)}</td>
                      <td className="verify-mono verify-size">{formatBytes(item.shareFileSize)}</td>
                    </tr>
                  ))}
                  {filteredItems.length === 0 && (
                    <tr>
                      <td colSpan={4} className="explorer-message">
                        {filter === 'differences' && !searchLower ? 'No differences. Every compared file matches.' : 'No files match this filter.'}
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
            {filteredItems.length > MAX_ROWS && (
              <div className="verify-row-limit">
                Showing the first {MAX_ROWS.toLocaleString()} of {filteredItems.length.toLocaleString()} files. Download the CSV for the full list.
              </div>
            )}
          </div>
        </>
      )}
    </div>
  );
}
